using System;
using System.Collections.Generic;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Turns a raw freehand cursor path into a clean, smooth curve (the "Draw" tool). First drops points that are
    /// too close together (de-noise), then rounds the corners with a couple of Chaikin corner-cutting passes -
    /// giving the Illustrator-like smoothing the modder expects without fitting actual beziers.
    /// </summary>
    public static class StrokeSmoothing
    {
        public static List<Vector2> Smooth(IReadOnlyList<Vector2> raw, float minSpacing = 4f, int iterations = 2)
        {
            var pts = simplify(raw, minSpacing);
            if (pts.Count < 3)
                return pts;

            for (int it = 0; it < iterations; it++)
                pts = chaikin(pts);

            return pts;
        }

        /// <summary>Keeps the first/last point and drops any point closer than <paramref name="minSpacing"/> to the last kept one.</summary>
        private static List<Vector2> simplify(IReadOnlyList<Vector2> raw, float minSpacing)
        {
            var result = new List<Vector2>();
            if (raw.Count == 0)
                return result;

            result.Add(raw[0]);
            float minSq = minSpacing * minSpacing;
            for (int i = 1; i < raw.Count; i++)
            {
                if ((raw[i] - result[^1]).LengthSquared >= minSq)
                    result.Add(raw[i]);
            }

            // Always keep the genuine endpoint so the stroke reaches where the cursor was released.
            if (raw.Count > 1 && (raw[^1] - result[^1]).LengthSquared > 0.01f)
                result.Add(raw[^1]);

            return result;
        }

        /// <summary>One Chaikin corner-cutting pass (keeps the endpoints, rounds everything between).</summary>
        private static List<Vector2> chaikin(List<Vector2> pts)
        {
            var result = new List<Vector2> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[i + 1];
                result.Add(a + (b - a) * 0.25f);
                result.Add(a + (b - a) * 0.75f);
            }
            result.Add(pts[^1]);
            return result;
        }
    }
}
