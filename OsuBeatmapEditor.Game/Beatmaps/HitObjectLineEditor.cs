using System;
using System.Collections.Generic;
using System.Globalization;

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

        /// <summary>Shifts the object's time (and a spinner's end time) by <paramref name="deltaMs"/>.</summary>
        public static string ShiftTime(string raw, int deltaMs)
        {
            string[] parts = raw.Split(',');
            if (parts.Length < 4 || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int time))
                return raw;

            parts[2] = (time + deltaMs).ToString(CultureInfo.InvariantCulture);

            // Spinners (type bit 3) carry an end time in parts[5]; keep it in step.
            if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                && (type & 0b1000) != 0
                && parts.Length >= 6
                && int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
            {
                parts[5] = (end + deltaMs).ToString(CultureInfo.InvariantCulture);
            }

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
