using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Targeted, lossless edits to a raw <c>.osu</c> hit-object line: only the fields that change are
    /// rewritten, everything else (hitsounds, slider type, sample filename, etc.) is preserved verbatim.
    /// Mirrors how osu!lazer keeps unrelated data intact when nudging an object.
    /// </summary>
    public static class HitObjectLineEditor
    {
        /// <summary>Toggles the object's "new combo" flag (type bit 2) on its raw line.</summary>
        public static string ToggleNewCombo(string raw)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 4 || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                return raw;

            type ^= 0b100;
            parts[3] = type.ToString(CultureInfo.InvariantCulture);
            return string.Join(',', parts);
        }

        /// <summary>Whether the raw line's type flags it as a new combo.</summary>
        public static bool HasNewCombo(string raw)
        {
            string[] parts = raw.Split(',');
            return parts.Length >= 4 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) && (type & 0b100) != 0;
        }

        /// <summary>
        /// Shifts the object's time (and a spinner's end time) by <paramref name="deltaMs"/>. Times are parsed
        /// and re-emitted as doubles so lazer's fractional object times (osu file format v128) round-trip intact.
        /// </summary>
        public static string ShiftTime(string raw, double deltaMs)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 4 || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                return raw;

            parts[2] = (time + deltaMs).ToString(CultureInfo.InvariantCulture);

            // Spinners (type bit 3) carry an end time in parts[5]; keep it in step.
            if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                && (type & 0b1000) != 0
                && parts.Length >= 6
                && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double end))
            {
                parts[5] = (end + deltaMs).ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(',', parts);
        }

        /// <summary>Sets a spinner's end time (parts[5]), changing its duration. No-op for non-spinners.</summary>
        public static string SetSpinnerEndTime(string raw, int endTime)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 6
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                || (type & 0b1000) == 0)
                return raw;

            parts[5] = endTime.ToString(CultureInfo.InvariantCulture);
            return string.Join(',', parts);
        }

        /// <summary>Offsets the object's position (and a slider's control points) by (dx, dy).</summary>
        public static string ShiftPosition(string raw, int dx, int dy)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 4
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return raw;

            parts[0] = ((int)Math.Round(x) + dx).ToString(CultureInfo.InvariantCulture);
            parts[1] = ((int)Math.Round(y) + dy).ToString(CultureInfo.InvariantCulture);

            // Sliders (type bit 1) store anchors as "<type>|x:y|x:y|..." in parts[5].
            if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                && (type & 0b10) != 0
                && parts.Length >= 6)
            {
                parts[5] = shiftCurve(parts[5], dx, dy);
            }

            return string.Join(',', parts);
        }

        /// <summary>
        /// Rewrites a slider's head position (parts 0/1), curve field (part 5) and pixel length (part 7) from
        /// edited control points, leaving the slide count and hitsounds untouched. Used by control-point editing.
        /// </summary>
        public static string SetSliderCurve(string raw, IReadOnlyList<SliderControlPoint> controlPoints, double pixelLength)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 6 || controlPoints.Count == 0)
                return raw;

            parts[0] = ((int)Math.Round(controlPoints[0].X)).ToString(CultureInfo.InvariantCulture);
            parts[1] = ((int)Math.Round(controlPoints[0].Y)).ToString(CultureInfo.InvariantCulture);
            parts[5] = SliderGeometry.CurveField(controlPoints);
            if (parts.Length >= 8)
                parts[7] = pixelLength.ToString("0.###", CultureInfo.InvariantCulture);
            return string.Join(',', parts);
        }

        /// <summary>Sets a slider's repeat/slide count (the <c>slides</c> field) in its raw line.</summary>
        public static string SetSliderSlides(string raw, int slides)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 7)
                return raw;

            parts[6] = slides.ToString(CultureInfo.InvariantCulture);
            return string.Join(',', parts);
        }

        /// <summary>Sets the object's hitSound bitfield (part 4: bit1 whistle, bit2 finish, bit3 clap).</summary>
        public static string SetHitSound(string raw, int hitSound)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 5)
                return raw;

            parts[4] = hitSound.ToString(CultureInfo.InvariantCulture);
            return string.Join(',', parts);
        }

        /// <summary>
        /// Sets the object's normal and addition sample banks in its <c>hitSample</c> field
        /// (the trailing "normalSet:additionSet:index:volume:filename"), preserving index/volume/filename.
        /// </summary>
        public static string SetSampleBanks(string raw, SampleBank normal, SampleBank addition)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 5)
                return raw;

            // The hitSample field is the trailing colon-delimited token; add one if the line omits it.
            string last = parts[^1];
            string[] hs = last.Contains(':') ? last.Split(':') : new[] { "0", "0", "0", "0", "" };
            if (hs.Length < 5)
                System.Array.Resize(ref hs, 5);
            for (int i = 0; i < hs.Length; i++)
                hs[i] ??= string.Empty;

            hs[0] = ((int)bankToSet(normal)).ToString(CultureInfo.InvariantCulture);
            hs[1] = ((int)bankToSet(addition)).ToString(CultureInfo.InvariantCulture);

            parts[^1] = string.Join(':', hs);
            return string.Join(',', parts);
        }

        /// <summary>
        /// Rewrites a slider's per-node hitsounds: <c>edgeSounds</c> (part 8) and <c>edgeSets</c> (part 9).
        /// No-op unless the line is a slider with a curve and length already present (parts 5-7).
        /// </summary>
        public static string SetSliderNodeSamples(string raw, IReadOnlyList<NodeSample> nodes)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 8 || nodes.Count == 0
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                || (type & 0b10) == 0)
                return raw;

            string sounds = string.Join('|', nodes.Select(n => n.HitSound.ToString(CultureInfo.InvariantCulture)));
            string sets = string.Join('|', nodes.Select(n => $"{(int)bankToSet(n.NormalBank)}:{(int)bankToSet(n.AdditionBank)}"));

            // edgeSounds and edgeSets sit between length (7) and hitSample (last). Insert them if absent.
            if (parts.Length <= 8)
            {
                var list = new List<string>(parts) { sounds, sets };
                parts = list.ToArray();
            }
            else
            {
                parts[8] = sounds;
                if (parts.Length > 9)
                    parts[9] = sets;
                else
                {
                    var list = new List<string>(parts) { sets };
                    parts = list.ToArray();
                }
            }

            return string.Join(',', parts);
        }

        /// <summary>The .osu sample-set integer for a bank (Normal = 1, Soft = 2, Drum = 3).</summary>
        public static int SampleSet(SampleBank bank) => bankToSet(bank);

        private static int bankToSet(SampleBank bank) => bank switch
        {
            SampleBank.Normal => 1,
            SampleBank.Soft => 2,
            SampleBank.Drum => 3,
            _ => 0, // Auto -> 0 (inherit the timing point's sample set)
        };

        private static string shiftCurve(string curve, int dx, int dy)
        {
            string[] tokens = curve.Split('|');

            // tokens[0] is the curve-type letter (B/L/P/C); the rest are "x:y" anchors.
            for (int i = 1; i < tokens.Length; i++)
            {
                string[] xy = tokens[i].Split(':');
                if (xy.Length == 2
                    && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                    && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                {
                    tokens[i] = $"{(int)Math.Round(px) + dx}:{(int)Math.Round(py) + dy}";
                }
            }

            return string.Join('|', tokens);
        }
    }
}
