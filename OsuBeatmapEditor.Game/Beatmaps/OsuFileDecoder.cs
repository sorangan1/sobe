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
            var comboColours = new SortedDictionary<int, osu.Framework.Graphics.Colour4>();
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
                        parseKeyValue(line, "SliderTickRate", v => setFloat(v, x => result.SliderTickRate = x));
                        break;

                    case "Events":
                        parseEventLine(line, result);
                        break;

                    case "Colours":
                        parseComboColour(line, comboColours);
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

            // Assign each timing point a stable id, then derive the timeline/SV/kiai lists from them.
            for (int i = 0; i < result.TimingPointModels.Count; i++)
                result.TimingPointModels[i] = result.TimingPointModels[i] with { Id = i };

            result.ComboColours.AddRange(comboColours.Values);

            result.RebuildTimingDerived();
            return result;
        }

        /// <summary>Parses a <c>[Colours]</c> <c>ComboN : r,g,b</c> line into <paramref name="into"/> (keyed by N).</summary>
        private static void parseComboColour(string line, SortedDictionary<int, osu.Framework.Graphics.Colour4> into)
        {
            int sep = line.IndexOf(':');
            if (sep < 0)
                return;

            string key = line[..sep].Trim();
            if (!key.StartsWith("Combo", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(key.AsSpan("Combo".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return;

            string[] parts = line[(sep + 1)..].Split(',');
            if (parts.Length < 3
                || !byte.TryParse(parts[0].Trim(), out byte r)
                || !byte.TryParse(parts[1].Trim(), out byte g)
                || !byte.TryParse(parts[2].Trim(), out byte b))
                return;

            into[n] = new osu.Framework.Graphics.Colour4(r, g, b, 255);
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

            // Break event: 2,startTime,endTime (or "Break,start,end").
            string kind = parts[0].Trim();
            if ((kind == "2" || kind.Equals("Break", StringComparison.OrdinalIgnoreCase)) && parts.Length >= 3
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double start)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double end)
                && end > start)
            {
                result.Breaks.Add(new BreakPeriod((int)start, (int)end));
            }
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

            // effects (parts[7]) bit 0 = kiai; keep the whole field so other bits round-trip losslessly.
            int effects = parts.Length >= 8 && int.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ef) ? ef : 0;
            bool kiai = (effects & 1) != 0;

            // meter (parts[2]): beats per bar for an uninherited line.
            int meter = parts.Length >= 3 && int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0 ? m : 4;

            // sampleSet (parts[3]): 0 = none/default (Normal), 1 = Normal, 2 = Soft, 3 = Drum.
            int sampleSet = parts.Length >= 4 && int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ss) ? ss : 0;

            // sampleIndex (parts[4]): custom sample bank index; preserved for lossless re-emit.
            int sampleIndex = parts.Length >= 5 && int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int si) ? si : 0;

            // volume (parts[5]): 0-100; the active value applies to objects that don't override it.
            int volume = parts.Length >= 6 && int.TryParse(parts[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int vol) ? vol : 100;

            points.Add(new TimingPoint(time, beatLength, uninherited, kiai, sampleSet, volume));
            result.TimingPointModels.Add(new TimingPointModel(
                result.TimingPointModels.Count, time, beatLength, meter, sampleSet, sampleIndex, volume, uninherited, effects, line));
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


        private static SampleBank toBank(int sampleSet) => sampleSet switch
        {
            1 => SampleBank.Normal,
            2 => SampleBank.Soft,
            3 => SampleBank.Drum,
            _ => SampleBank.Auto, // 0 (or unknown) = inherit the active timing point's set, resolved at playback
        };

        /// <summary>Reads the object's hitSound bitfield and resolves its sample banks and volume.</summary>
        private static (int hitSound, SampleBank normal, SampleBank addition, float volume, int index, string filename) parseHitSounds(string[] parts, double time, List<TimingPoint> timingPoints)
        {
            int hitSound = parts.Length > 4 && int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hs) ? hs : 0;

            // hitSample (last field) "normalSet:additionSet:index:volume:filename" overrides bank/volume/index/file.
            int normalSet = 0, additionSet = 0, sampleVolume = 0, sampleIndex = 0;
            string filename = "";
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

                // index (custom sample bank) -> a numeric suffix at playback (soft-hitclap2); 0 = inherit timing point.
                if (hsParts.Length >= 3)
                    int.TryParse(hsParts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleIndex);

                if (hsParts.Length >= 4)
                    int.TryParse(hsParts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleVolume);

                // filename = a fully custom sample file packed with the map (overrides the normal sample at playback).
                if (hsParts.Length >= 5)
                    filename = hsParts[4].Trim();
            }

            // Banks are kept RAW (0 -> Auto): the inherit chain (addition->normal->timing point) is resolved at
            // playback (EditorScreen.resolveBanksAt) so an "Auto" bank follows the timing points like in osu!.
            // Store ONLY the object's explicit volume override (hitSample volume); 0 = inherit, also resolved per
            // sample-event at its own time during playback (EditorScreen.volumeAt).
            float volume = sampleVolume > 0 ? Math.Clamp(sampleVolume / 100f, 0f, 1f) : 0f;

            return (hitSound, toBank(normalSet), toBank(additionSet), volume, sampleIndex, filename);
        }

        /// <summary>
        /// Resolves a slider's per-node hitsounds from its <c>edgeSounds</c> (parts[8]) and <c>edgeSets</c>
        /// (parts[9]) fields - one <see cref="NodeSample"/> per node (head, each repeat, tail; count = slides + 1).
        /// Missing fields fall back to the object's own hitSound and banks (legacy maps), and a node set of 0
        /// resolves to the active timing-point sample set, exactly like the object-level resolution.
        /// </summary>
        private static IReadOnlyList<NodeSample> parseNodeSamples(string[] parts, int slides, double time, List<TimingPoint> timingPoints, int objHitSound, SampleBank objNormal, SampleBank objAddition)
        {
            int nodeCount = slides + 1;

            string[] sounds = parts.Length > 8 ? parts[8].Split('|') : System.Array.Empty<string>();
            string[] sets = parts.Length > 9 ? parts[9].Split('|') : System.Array.Empty<string>();

            var result = new NodeSample[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                int hs = i < sounds.Length && int.TryParse(sounds[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : objHitSound;

                // Banks kept raw (0 -> Auto); a missing edgeSet falls back to the object's own banks. The
                // inherit chain is resolved at playback (EditorScreen.resolveBanksAt), like the object level.
                SampleBank normal = objNormal, addition = objAddition;
                if (i < sets.Length)
                {
                    string[] ns = sets[i].Split(':');
                    if (ns.Length >= 2
                        && int.TryParse(ns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int normalSet)
                        && int.TryParse(ns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int additionSet))
                    {
                        normal = toBank(normalSet);
                        addition = toBank(additionSet);
                    }
                }

                result[i] = new NodeSample(hs, normal, addition);
            }

            return result;
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

            (int hitSound, SampleBank normalBank, SampleBank additionBank, float sampleVolume, int sampleIndex, string sampleFilename) = parseHitSounds(parts, time, timingPoints);

            // Type bitfield: bit 0 = circle, bit 1 = slider, bit 3 = spinner.
            if ((type & 0b1000) != 0)
            {
                // Spinner format: x,y,time,type,hitSound,endTime,hitSample - parts[5] is the end time.
                double endTime = parts.Length >= 6 && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double et) ? et : time;
                double spinnerDuration = Math.Max(0, endTime - time);

                result.HitObjects.Add(new HitObjectModel(x, y, time, HitObjectKind.Spinner, null, Duration: spinnerDuration,
                    ComboNumber: comboNumber, ComboIndex: comboIndex,
                    HitSound: hitSound, NormalBank: normalBank, AdditionBank: additionBank, SampleVolume: sampleVolume, RawLine: line,
                    SampleIndex: sampleIndex, SampleFilename: sampleFilename));
            }
            else if ((type & 0b10) != 0 && parts.Length >= 6)
            {
                var controlPoints = SliderGeometry.ParseControlPoints(line, result.FormatVersion);
                (var path, double duration, int slides) = parseSlider(time, parts, timingPoints, sliderMultiplier, controlPoints);
                var nodeSamples = parseNodeSamples(parts, slides, time, timingPoints, hitSound, normalBank, additionBank);
                result.HitObjects.Add(new HitObjectModel(x, y, time, HitObjectKind.Slider, path, duration, slides, comboNumber, comboIndex,
                    hitSound, normalBank, additionBank, sampleVolume, line, ControlPoints: controlPoints, NodeSamples: nodeSamples,
                    SampleIndex: sampleIndex, SampleFilename: sampleFilename));
            }
            else
            {
                result.HitObjects.Add(new HitObjectModel(x, y, time, HitObjectKind.Circle, null, ComboNumber: comboNumber, ComboIndex: comboIndex,
                    HitSound: hitSound, NormalBank: normalBank, AdditionBank: additionBank, SampleVolume: sampleVolume, RawLine: line,
                    SampleIndex: sampleIndex, SampleFilename: sampleFilename));
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
