using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// A minimal decoder for the .osu text format. It extracts what the editor playfield needs:
    /// audio filename, CircleSize, and hit objects (circles + slider paths, with slider durations
    /// derived from timing points / SliderMultiplier). Version-agnostic across "osu file format vN".
    /// </summary>
    public static class OsuFileDecoder
    {
        private readonly record struct TimingPoint(double Time, double BeatLength, bool Uninherited, bool Kiai, int SampleSet, int Volume);

        public static ParsedBeatmap Decode(string path)
        {
            using var reader = new StreamReader(path);
            return Decode(reader);
        }

        public static ParsedBeatmap Decode(TextReader reader)
        {
            var result = new ParsedBeatmap();
            var timingPoints = new List<TimingPoint>();
            float sliderMultiplier = 1.4f;
            int comboNumber = 0;
            int comboIndex = 0;
            bool firstHitObject = true;
            string section = string.Empty;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Header line: "osu file format vN" (controls some slider parsing edge cases).
                if (section.Length == 0 && line.StartsWith("osu file format v", StringComparison.Ordinal)
                    && int.TryParse(line.AsSpan("osu file format v".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver))
                {
                    result.FormatVersion = ver;
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1];

                    // Timing points always precede hit objects; order them once before we need them.
                    if (section == "HitObjects")
                        timingPoints.Sort((a, b) => a.Time.CompareTo(b.Time));

                    continue;
                }

                switch (section)
                {
                    case "General":
                        parseKeyValue(line, "AudioFilename", v => result.AudioFilename = v);
                        parseKeyValue(line, "PreviewTime", v =>
                        {
                            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pt))
                                result.PreviewTime = pt;
                        });
                        parseKeyValue(line, "StackLeniency", v => setFloat(v, x => result.StackLeniency = x));
                        break;

                    case "Editor":
                        parseKeyValue(line, "Bookmarks", v => parseBookmarks(v, result));
                        break;

                    case "Metadata":
                        parseKeyValue(line, "Title", v => result.Title = v);
                        parseKeyValue(line, "TitleUnicode", v => result.TitleUnicode = v);
                        parseKeyValue(line, "Artist", v => result.Artist = v);
                        parseKeyValue(line, "ArtistUnicode", v => result.ArtistUnicode = v);
                        parseKeyValue(line, "Creator", v => result.Creator = v);
                        parseKeyValue(line, "Version", v => result.Version = v);
                        parseKeyValue(line, "Source", v => result.Source = v);
                        parseKeyValue(line, "Tags", v => result.Tags = v);
                        break;

                    case "Difficulty":
                        parseKeyValue(line, "HPDrainRate", v => setFloat(v, x => result.HpDrainRate = x));
                        parseKeyValue(line, "CircleSize", v => setFloat(v, x => result.CircleSize = x));
                        parseKeyValue(line, "OverallDifficulty", v => setFloat(v, x => result.OverallDifficulty = x));
                        parseKeyValue(line, "ApproachRate", v => setFloat(v, x => result.ApproachRate = x));
                        parseKeyValue(line, "SliderMultiplier", v => setFloat(v, x => { sliderMultiplier = x; result.SliderMultiplier = x; }));
                        break;

                    case "Events":
                        parseEventLine(line, result);
                        break;

                    case "TimingPoints":
                        parseTimingPoint(line, timingPoints, result);
                        break;

                    case "HitObjects":
                        parseHitObjectLine(line, result, timingPoints, sliderMultiplier, ref comboNumber, ref comboIndex, ref firstHitObject);
                        break;
                }
            }

            // Assign each object a stable id so the editor can track selection/edits across reordering.
            for (int i = 0; i < result.HitObjects.Count; i++)
                result.HitObjects[i] = result.HitObjects[i] with { Id = i };

            computeKiaiSections(timingPoints, result);
            buildVelocityPoints(timingPoints, result);
            return result;
        }

        /// <summary>Bakes the effective slider-velocity multiplier over time: 1 at each red line, -100/beatLength at greens.</summary>
        private static void buildVelocityPoints(List<TimingPoint> points, ParsedBeatmap result)
        {
            double sv = 1;
            foreach (var tp in points.OrderBy(p => p.Time))
            {
                sv = tp.Uninherited ? 1 : Math.Clamp(-100 / tp.BeatLength, 0.1, 10);
                result.VelocityPoints.Add(new VelocityPoint(tp.Time, sv));
            }
        }

        private static void computeKiaiSections(List<TimingPoint> points, ParsedBeatmap result)
        {
            bool active = false;
            int start = 0;

            foreach (var tp in points.OrderBy(p => p.Time))
            {
                if (tp.Kiai && !active)
                {
                    active = true;
                    start = (int)tp.Time;
                }
                else if (!tp.Kiai && active)
                {
                    active = false;
                    result.KiaiSections.Add(new KiaiSection(start, (int)tp.Time));
                }
            }

            if (active)
                result.KiaiSections.Add(new KiaiSection(start, int.MaxValue));
        }

        private static void parseKeyValue(string line, string key, Action<string> apply)
        {
            int sep = line.IndexOf(':');
            if (sep < 0)
                return;

            if (line[..sep].Trim() == key)
                apply(line[(sep + 1)..].Trim());
        }

        private static void setFloat(string value, Action<float> apply)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                apply(f);
        }

        private static void parseEventLine(string line, ParsedBeatmap result)
        {
            // Background event: 0,0,"filename.jpg"[,x,y]
            string[] parts = line.Split(',');
            if (result.BackgroundFilename.Length == 0 && parts.Length >= 3 && parts[0].Trim() == "0")
                result.BackgroundFilename = parts[2].Trim().Trim('"');
        }

        private static void parseTimingPoint(string line, List<TimingPoint> points, ParsedBeatmap result)
        {
            string[] parts = line.Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength))
                return;

            // Newer formats carry an explicit uninherited flag; older ones use the sign of beatLength.
            bool uninherited = parts.Length >= 7 ? parts[6].Trim() == "1" : beatLength > 0;

            // effects (parts[7]) bit 0 = kiai.
            bool kiai = parts.Length >= 8
                        && int.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int effects)
                        && (effects & 1) != 0;

            // sampleSet (parts[3]): 0 = none/default (Normal), 1 = Normal, 2 = Soft, 3 = Drum.
            int sampleSet = parts.Length >= 4 && int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ss) ? ss : 0;

            // volume (parts[5]): 0-100; the active value applies to objects that don't override it.
            int volume = parts.Length >= 6 && int.TryParse(parts[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int vol) ? vol : 100;

            points.Add(new TimingPoint(time, beatLength, uninherited, kiai, sampleSet, volume));

            // Display value: BPM for red (uninherited) lines, the SV multiplier for green (inherited) ones.
            double markerValue = uninherited
                ? (beatLength > 0 ? 60000.0 / beatLength : 0)
                : Math.Clamp(-100 / beatLength, 0.1, 10);
            result.TimingPoints.Add(new TimingMarker((int)time, uninherited, markerValue));

            if (uninherited)
            {
                int meter = parts.Length >= 3 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0 ? m : 4;
                result.BeatPoints.Add(new BeatPoint(time, beatLength, meter));
            }
        }

        private static void parseBookmarks(string value, ParsedBeatmap result)
        {
            foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t))
                    result.Bookmarks.Add(t);
            }
        }

        private static (double beatLength, double sv) effectiveTiming(double time, List<TimingPoint> points)
        {
            double beatLength = 500; // 120 BPM fallback if a map somehow has no uninherited point
            double sv = 1;

            foreach (var p in points)
            {
                if (p.Time > time)
                    break;

                if (p.Uninherited)
                {
                    beatLength = p.BeatLength;
                    sv = 1;
                }
                else
                {
                    sv = Math.Clamp(-100 / p.BeatLength, 0.1, 10);
                }
            }

            return (beatLength, sv);
        }

        /// <summary>The sample set (1=Normal, 2=Soft, 3=Drum; 0=default) active at the given time.</summary>
        private static int effectiveSampleSet(double time, List<TimingPoint> points)
        {
            int set = 0;
            foreach (var p in points)
            {
                if (p.Time > time)
                    break;
                set = p.SampleSet;
            }
            return set;
        }

        /// <summary>The hitsound volume (0-100) active at the given time.</summary>
        private static int effectiveVolume(double time, List<TimingPoint> points)
        {
            int volume = 100;
            foreach (var p in points)
            {
                if (p.Time > time)
                    break;
                volume = p.Volume;
            }
            return volume;
        }

        private static SampleBank toBank(int sampleSet) => sampleSet switch
        {
            2 => SampleBank.Soft,
            3 => SampleBank.Drum,
            _ => SampleBank.Normal,
        };

        /// <summary>Reads the object's hitSound bitfield and resolves its sample banks and volume.</summary>
        private static (int hitSound, SampleBank normal, SampleBank addition, float volume) parseHitSounds(string[] parts, double time, List<TimingPoint> timingPoints)
        {
            int hitSound = parts.Length > 4 && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hs) ? hs : 0;

            // hitSample (last field) "normalSet:additionSet:index:volume:filename" overrides bank/volume.
            int normalSet = 0, additionSet = 0, sampleVolume = 0;
            string last = parts[^1];
            if (last.Contains(':'))
            {
                string[] hsParts = last.Split(':');
                if (hsParts.Length >= 2
                    && int.TryParse(hsParts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ns)
                    && int.TryParse(hsParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int adds))
                {
                    normalSet = ns;
                    additionSet = adds;
                }

                if (hsParts.Length >= 4)
                    int.TryParse(hsParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleVolume);
            }

            int timingSet = effectiveSampleSet(time, timingPoints);
            int resolvedNormal = normalSet > 0 ? normalSet : timingSet;
            int resolvedAddition = additionSet > 0 ? additionSet : resolvedNormal;

            // The object's own sample volume overrides the timing point's when non-zero.
            int volume = sampleVolume > 0 ? sampleVolume : effectiveVolume(time, timingPoints);

            return (hitSound, toBank(resolvedNormal), toBank(resolvedAddition), Math.Clamp(volume / 100f, 0f, 1f));
        }

        private static void parseHitObjectLine(string line, ParsedBeatmap result, List<TimingPoint> timingPoints, float sliderMultiplier, ref int comboNumber, ref int comboIndex, ref bool firstHitObject)
        {
            // Format: x,y,time,type,hitSound,objectParams...,hitSample
            string[] parts = line.Split(',');
            if (parts.Length < 4)
                return;

            // Time is integer in classic formats but may be fractional in newer ones (e.g. v128).
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                return;

            // Combo number resets to 1 whenever the "new combo" flag (bit 2) is set.
            bool newCombo = (type & 0b100) != 0;
            comboNumber = newCombo ? 1 : comboNumber + 1;

            // Combo colour index advances on each new combo, honouring the colour-skip bits (type >> 4).
            // The very first object always starts on colour 0, even if it isn't flagged "new combo".
            if (newCombo && !firstHitObject)
                comboIndex += 1 + ((type >> 4) & 0b111);
            firstHitObject = false;

            (int hitSound, SampleBank normalBank, SampleBank additionBank, float sampleVolume) = parseHitSounds(parts, time, timingPoints);

            // Type bitfield: bit 0 = circle, bit 1 = slider, bit 3 = spinner.
            if ((type & 0b1000) != 0)
            {
                result.HitObjects.Add(new HitObjectModel(x, y, (int)time, HitObjectKind.Spinner, null, ComboNumber: comboNumber, ComboIndex: comboIndex,
                    HitSound: hitSound, NormalBank: normalBank, AdditionBank: additionBank, SampleVolume: sampleVolume, RawLine: line));
            }
            else if ((type & 0b10) != 0 && parts.Length >= 6)
            {
                var controlPoints = SliderGeometry.ParseControlPoints(line, result.FormatVersion);
                (var path, double duration, int slides) = parseSlider(time, parts, timingPoints, sliderMultiplier, controlPoints);
                result.HitObjects.Add(new HitObjectModel(x, y, (int)time, HitObjectKind.Slider, path, duration, slides, comboNumber, comboIndex,
                    hitSound, normalBank, additionBank, sampleVolume, line, ControlPoints: controlPoints));
            }
            else
            {
                result.HitObjects.Add(new HitObjectModel(x, y, (int)time, HitObjectKind.Circle, null, ComboNumber: comboNumber, ComboIndex: comboIndex,
                    HitSound: hitSound, NormalBank: normalBank, AdditionBank: additionBank, SampleVolume: sampleVolume, RawLine: line));
            }
        }

        private static (IReadOnlyList<Vector2> path, double duration, int slides) parseSlider(
            double time, string[] parts, List<TimingPoint> timingPoints, float sliderMultiplier, IReadOnlyList<SliderControlPoint> controlPoints)
        {
            // parts[6] = slides (repeats), parts[7] = pixel length.
            int slides = 1;
            if (parts.Length >= 7)
                int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out slides);
            slides = Math.Max(1, slides);

            double pixelLength = 0;
            if (parts.Length >= 8)
                double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out pixelLength);

            // Path geometry mirrors lazer's SliderPath (segmented spline + length trim/extend to pixelLength).
            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);

            // Slider travel time: pixelLength / velocity, velocity = SliderMultiplier * 100 * SV per beat.
            (double beatLength, double sv) = effectiveTiming(time, timingPoints);
            double spanDuration = pixelLength > 0 && sliderMultiplier > 0
                ? pixelLength * beatLength / (sliderMultiplier * 100 * sv)
                : 0;
            double duration = Math.Max(60, spanDuration * slides); // floor so it stays briefly visible even if timing is odd

            return (path, duration, slides);
        }
    }
}
