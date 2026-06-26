using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OsuBeatmapEditor.Game.Beatmaps;
using osuTK;
using Realms;

namespace OsuBeatmapEditor.ReplayAnalysis
{
    /// <summary>
    /// Offline tool: mines osu!lazer replays (from its client.realm + file store) to measure how a real cursor
    /// actually moves, and prints calibrated parameters for the editor's "humanise" Auto cursor. Read-only; it
    /// copies the realm before reading, exactly like <see cref="BeatmapStore"/>.
    ///
    /// What it measures, all keyed by osu!'s normalised jump distance (distance * 50 / circleRadius; one diameter = 100):
    ///   - aim error      : how far off the circle centre the cursor is at hit time (fraction of radius)
    ///   - overshoot      : how far past the target the cursor flies before settling (fraction of the jump)
    ///   - arc            : lateral bow off the straight line between objects (fraction of the jump)
    ///   - slider release : how early the cursor leaves a slider before its end (ms and fraction of duration)
    ///   - stack movement : cursor travel across a stacked pair (fraction of radius; expected ~0)
    /// </summary>
    public static class Program
    {
        // Legacy mod bits we must exclude (they change geometry or timing): EZ, HR, DT, HT, NC, FL.
        private const int mod_ez = 2, mod_hr = 16, mod_dt = 64, mod_ht = 256, mod_nc = 512, mod_fl = 1024;
        private const int excluded_mods = mod_ez | mod_hr | mod_dt | mod_ht | mod_nc | mod_fl;

        // Normalised-distance bin edges (osu! units; diameter = 100). Aim is keyed by the incoming jump.
        private static readonly double[] binEdges = { 0, 25, 50, 75, 100, 130, 160, 200, 260, 340, 450, 600, 800, 1100, double.PositiveInfinity };

        public static int Main(string[] args)
        {
            string? dataDir = LazerStorage.FindDataDirectory();
            if (dataDir == null)
            {
                Console.Error.WriteLine("Could not locate the osu!lazer data directory.");
                return 1;
            }

            string realmFile = Path.Combine(dataDir, "client.realm");
            if (!File.Exists(realmFile))
            {
                Console.Error.WriteLine($"No client.realm at {realmFile}");
                return 1;
            }

            string tempCopy = Path.Combine(Path.GetTempPath(), $"osu-replay-analysis-{Guid.NewGuid():N}.realm");
            File.Copy(realmFile, tempCopy, overwrite: true);

            var agg = new Aggregator(binEdges);
            int scoresSeen = 0, scoresUsed = 0, framesTotal = 0;

            try
            {
                var config = new RealmConfiguration(tempCopy) { IsDynamic = true };
                using var realm = Realm.GetInstance(config);

                foreach (dynamic score in realm.DynamicApi.All("Score"))
                {
                    scoresSeen++;
                    try
                    {
                        if (!tryAnalyseScore(score, dataDir, agg, out int frames))
                            continue;
                        scoresUsed++;
                        framesTotal += frames;
                    }
                    catch
                    {
                        // One bad score shouldn't abort the run.
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to read realm: {ex.Message}");
                return 1;
            }
            finally
            {
                try { File.Delete(tempCopy); } catch { /* best effort */ }
            }

            Console.WriteLine($"Scores in realm : {scoresSeen}");
            Console.WriteLine($"Scores analysed : {scoresUsed} (osu! Standard, no EZ/HR/DT/HT/NC/FL, sane alignment)");
            Console.WriteLine($"Cursor frames   : {framesTotal:N0}");
            Console.WriteLine();

            if (scoresUsed == 0)
            {
                Console.WriteLine("No usable replays found.");
                return 0;
            }

            agg.Report();
            return 0;
        }

        private static bool tryAnalyseScore(dynamic score, string dataDir, Aggregator agg, out int frameCount)
        {
            frameCount = 0;

            string ruleset = score.Ruleset?.ShortName ?? string.Empty;
            if (ruleset != "osu")
                return false;

            dynamic? beatmap = score.BeatmapInfo;
            if (beatmap == null)
                return false;

            string beatmapHash = beatmap.Hash ?? string.Empty;
            string? osuPath = beatmapHash.Length > 0 ? LazerFileStore.ResolvePath(dataDir, beatmapHash) : null;
            if (osuPath == null || !File.Exists(osuPath))
                return false;

            // Locate the .osr file usage on the score.
            string? osrHash = null;
            foreach (dynamic usage in score.Files)
            {
                string filename = usage.Filename ?? string.Empty;
                if (filename.EndsWith(".osr", StringComparison.OrdinalIgnoreCase))
                {
                    osrHash = usage.File?.Hash;
                    break;
                }
            }

            string? osrPath = osrHash != null ? LazerFileStore.ResolvePath(dataDir, osrHash) : null;
            if (osrPath == null || !File.Exists(osrPath))
                return false;

            OsrReplay replay = OsrReplay.Parse(File.ReadAllBytes(osrPath));
            if (replay.GameMode != 0 || (replay.Mods & excluded_mods) != 0 || replay.Frames.Count < 16)
                return false;

            ParsedBeatmap map = OsuFileDecoder.Decode(osuPath);
            var objs = map.HitObjects;
            if (objs.Count < 8)
                return false;

            StackingProcessor.Apply(objs, map.Preempt, map.StackLeniency);

            float radius = map.CircleRadius;
            float diameter = radius * 2;
            float scaling = radius > 0.01f ? 50f / radius : 1f;
            var track = new CursorTrack(replay.Frames);

            // Sanity / HR-flip guard: if the cursor is wildly off the circles, the replay is misaligned (wrong map,
            // a flip mod we didn't catch, etc.) - discard the whole score rather than poison the stats.
            var aimSample = new List<double>();
            foreach (var o in objs)
            {
                Vector2 c = startCentre(o, diameter);
                aimSample.Add((track.PositionAt(o.StartTime) - c).Length / radius);
            }
            if (Median(aimSample) > 1.2)
                return false;

            extractFeatures(objs, track, radius, diameter, scaling, agg);
            analyseStreams(objs, track, radius, diameter, scaling, agg);
            analyseSliderLaziness(objs, track, radius, diameter, scaling, agg);
            frameCount = replay.Frames.Count;
            return true;
        }

        /// <summary>
        /// Measures how much the real cursor cuts each slider's path: the ratio of cursor travel to ball travel
        /// (1 = perfect trace, lower = lazier) and how far the cursor lags the ball (x radius ~ follow-circle slack),
        /// keyed by slider velocity. Only well-tracked sliders count (cursor inside the follow circle ~all the time).
        /// </summary>
        private static void analyseSliderLaziness(IReadOnlyList<HitObjectModel> objs, CursorTrack track, float radius, float diameter, float scaling, Aggregator agg)
        {
            foreach (var o in objs)
            {
                if (o.Kind != HitObjectKind.Slider || o.Path is not { Count: > 1 } || o.Duration < 60)
                    continue;

                double start = o.StartTime;
                double dur = o.Duration;
                double end = start + dur;
                double step = Math.Clamp(dur / 40.0, 4, 12);

                Vector2 prevBall = sliderBall(o, start, diameter);
                Vector2 prevCur = smoothCursor(track, start);
                double ballLen = 0, curLen = 0;
                int tracked = 0, total = 0;
                var dists = new List<double>();

                for (double t = start; t <= end; t += step)
                {
                    Vector2 ball = sliderBall(o, t, diameter);
                    Vector2 cur = smoothCursor(track, t); // light smoothing keeps tremor out of the travel length
                    if (t > start)
                    {
                        ballLen += (ball - prevBall).Length;
                        curLen += (cur - prevCur).Length;
                    }
                    prevBall = ball;
                    prevCur = cur;

                    double d = (track.PositionAt(t) - ball).Length;
                    dists.Add(d / radius);
                    total++;
                    if (d <= radius * 2.4f)
                        tracked++;
                }

                if (total < 4 || ballLen < radius || tracked < total * 0.8)
                    continue;

                double ratio = Math.Clamp(curLen / ballLen, 0, 2);
                double vel = ballLen * scaling / dur; // normalised px per ms
                agg.AddSliderLazy(vel, ratio, Median(dists));
            }
        }

        private static Vector2 smoothCursor(CursorTrack track, double t)
            => (track.PositionAt(t - 3) + track.PositionAt(t) + track.PositionAt(t + 3)) / 3f;

        /// <summary>
        /// Finds runs of ≥6 consecutive circles at near-constant small spacing/time (a stream) and measures how the
        /// REAL cursor behaves there: its normalised spacing, how far off the note centres it sits, and its
        /// high-frequency "shake" (deviation from a short moving-average of its own path) - the last tells us how much
        /// jitter, if any, a real stream actually has.
        /// </summary>
        private static void analyseStreams(IReadOnlyList<HitObjectModel> objs, CursorTrack track, float radius, float diameter, float scaling, Aggregator agg)
        {
            int i = 0;
            while (i < objs.Count - 1)
            {
                // Grow a run while the next step is a small, roughly constant-time circle-to-circle hop.
                int j = i;
                double firstGap = objs[i + 1].StartTime - objs[i].StartTime;
                while (j + 1 < objs.Count)
                {
                    var a = objs[j];
                    var b = objs[j + 1];
                    if (a.Kind != HitObjectKind.Circle || b.Kind != HitObjectKind.Circle)
                        break;
                    double gap = b.StartTime - a.StartTime;
                    double dn = (startCentre(b, diameter) - startCentre(a, diameter)).Length * scaling;
                    if (gap <= 0 || gap > 160 || Math.Abs(gap - firstGap) > 0.30 * firstGap || dn > 140)
                        break;
                    j++;
                }

                int runLen = j - i + 1;
                if (runLen >= 6)
                {
                    for (int k = i; k <= j; k++)
                    {
                        Vector2 c = startCentre(objs[k], diameter);
                        agg.AddStreamAim((track.PositionAt(objs[k].StartTime) - c).Length / radius);
                        if (k > i)
                            agg.AddStreamSpacing((c - startCentre(objs[k - 1], diameter)).Length * scaling);
                    }

                    // High-frequency shake: |cursor - shortMovingAverage(cursor)| sampled across the run, in radii.
                    double t0 = objs[i].StartTime, t1 = objs[j].StartTime;
                    for (double t = t0; t <= t1; t += 4)
                    {
                        Vector2 avg = Vector2.Zero;
                        int n = 0;
                        for (double w = -12; w <= 12; w += 4) { avg += track.PositionAt(t + w); n++; }
                        avg /= n;
                        agg.AddStreamShake((track.PositionAt(t) - avg).Length / radius);
                    }
                }

                i = Math.Max(j, i + 1);
            }
        }

        private static void extractFeatures(IReadOnlyList<HitObjectModel> objs, CursorTrack track, float radius, float diameter, float scaling, Aggregator agg)
        {
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];

                // Aim error at hit time, keyed by the incoming jump distance.
                Vector2 cStart = startCentre(o, diameter);
                double incoming = i > 0 ? (cStart - endCentre(objs[i - 1], diameter)).Length * scaling : 0;
                agg.AddAim(incoming, (track.PositionAt(o.StartTime) - cStart).Length / radius);

                // Slider release lead.
                if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } && o.Duration > 0)
                    addSliderLead(o, track, radius, diameter, agg);

                if (i + 1 >= objs.Count)
                    continue;

                var next = objs[i + 1];
                Vector2 a = endCentre(o, diameter);
                Vector2 b = startCentre(next, diameter);
                double from = endTime(o);
                double to = next.StartTime;
                float dist = (b - a).Length;
                if (to <= from || dist < 1f)
                    continue;

                double dNorm = dist * scaling;
                Vector2 u = (b - a) / dist;

                // Stacked pair: how much does the cursor actually move?
                if (dNorm < 30)
                    agg.AddStack(track.PathLength(from, to) / radius);

                // Lateral arc: max perpendicular excursion off the straight line A->B over the gap (+ its sign).
                float lateralMax = 0, signedAtApex = 0;
                const int samples = 24;
                for (int s = 1; s < samples; s++)
                {
                    double t = from + (to - from) * s / samples;
                    Vector2 p = track.PositionAt(t);
                    float signed = cross(u, p - a);
                    float lateral = Math.Abs(signed);
                    if (lateral > lateralMax) { lateralMax = lateral; signedAtApex = signed; }
                }
                agg.AddArc(dNorm, lateralMax / dist);

                // Handedness: which side the bow falls on, by movement direction. Only real jumps (skip tight spacing
                // where the "bow" is really just heading to the next note).
                if (dNorm > 60)
                    agg.AddArcCurl(Math.Atan2(u.Y, u.X), signedAtApex / dist);

                // Overshoot: furthest the cursor flies past B (along travel dir) around the moment it arrives.
                float overMax = 0;
                for (double t = to - 15; t <= to + 45; t += 3)
                {
                    float proj = Vector2.Dot(track.PositionAt(t) - b, u);
                    if (proj > overMax) overMax = proj;
                }
                agg.AddOvershoot(dNorm, Math.Max(0, overMax) / dist);
            }
        }

        private static void addSliderLead(HitObjectModel o, CursorTrack track, float radius, float diameter, Aggregator agg)
        {
            double start = o.StartTime;
            double end = endTime(o);
            float trackRadius = radius * 2f; // follow-circle-ish tolerance
            double lastGood = double.NegativeInfinity;

            for (double t = start; t <= end; t += 5)
            {
                Vector2 ball = sliderBall(o, t, diameter);
                if ((track.PositionAt(t) - ball).Length <= trackRadius)
                    lastGood = t;
            }

            if (double.IsNegativeInfinity(lastGood))
                return; // never tracked (a miss) - ignore

            double lead = Math.Max(0, end - lastGood);
            agg.AddSliderLead(lead, lead / Math.Max(1, o.Duration));
        }

        // --- geometry helpers (mirror Playfield's auto cursor) ---

        private static Vector2 stackOffset(HitObjectModel o, float diameter) => new Vector2(o.StackHeight * diameter * -0.05f);

        private static Vector2 startCentre(HitObjectModel o, float diameter)
        {
            if (o.Kind == HitObjectKind.Spinner)
                return new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2, ParsedBeatmap.PLAYFIELD_HEIGHT / 2);
            return new Vector2(o.X, o.Y) + stackOffset(o, diameter);
        }

        private static Vector2 endCentre(HitObjectModel o, float diameter)
        {
            switch (o.Kind)
            {
                case HitObjectKind.Slider when o.Path is { Count: > 0 } path:
                    return (o.Slides % 2 == 0 ? path[0] : path[^1]) + stackOffset(o, diameter);
                case HitObjectKind.Spinner:
                    return new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2, ParsedBeatmap.PLAYFIELD_HEIGHT / 2);
                default:
                    return new Vector2(o.X, o.Y) + stackOffset(o, diameter);
            }
        }

        private static Vector2 sliderBall(HitObjectModel o, double time, float diameter)
        {
            var path = o.Path!;
            double dur = Math.Max(1, o.Duration);
            double spanDur = dur / Math.Max(1, o.Slides);
            double elapsed = Math.Clamp(time - o.StartTime, 0, dur);
            int span = spanDur > 0 ? Math.Min(o.Slides - 1, (int)(elapsed / spanDur)) : 0;
            double within = spanDur > 0 ? (elapsed - span * spanDur) / spanDur : 0;
            double frac = span % 2 == 0 ? within : 1 - within;
            return SliderGeometry.PointAtFraction(path, Math.Clamp(frac, 0, 1)) + stackOffset(o, diameter);
        }

        private static double endTime(HitObjectModel o) => o.StartTime + (o.Kind == HitObjectKind.Slider ? Math.Max(0, o.Duration) : 0);

        private static float cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        public static double Median(List<double> values)
        {
            if (values.Count == 0)
                return 0;
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        }
    }
}
