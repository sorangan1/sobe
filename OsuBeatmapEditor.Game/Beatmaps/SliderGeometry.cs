using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Bridges a slider's editable control points (<see cref="SliderAnchor"/>, head first) and its on-disk
    /// representation: the <c>type|x:y|...</c> curve field plus the computed polyline. A "red anchor" (sharp
    /// corner / segment boundary) is stored in .osu as a doubled control point - exactly how osu!stable and
    /// the framework's path sampler split a Bézier into segments - so toggling a corner just doubles a point.
    /// </summary>
    public static class SliderGeometry
    {
        /// <summary>Parses the head + curve field of a raw slider line into logical anchors and a curve type.</summary>
        public static (List<SliderAnchor> anchors, char type) ParseAnchors(string rawLine)
        {
            string[] parts = rawLine.Split(',');
            var anchors = new List<SliderAnchor>();
            char type = 'B';

            if (parts.Length < 6)
                return (anchors, type);

            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float hx);
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hy);

            string[] curve = parts[5].Split('|');
            if (curve.Length > 0 && curve[0].Length > 0)
                type = curve[0][0];

            var raw = new List<Vector2> { new Vector2(hx, hy) };
            for (int i = 1; i < curve.Length; i++)
            {
                string[] xy = curve[i].Split(':');
                if (xy.Length == 2
                    && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float px)
                    && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                {
                    raw.Add(new Vector2(px, py));
                }
            }

            return (Collapse(raw), type);
        }

        /// <summary>Collapses a raw point list (with doubled segment boundaries) into logical red-flagged anchors.</summary>
        public static List<SliderAnchor> Collapse(IReadOnlyList<Vector2> rawPoints)
        {
            var anchors = new List<SliderAnchor>();
            foreach (var p in rawPoints)
            {
                if (anchors.Count > 0 && anchors[^1].Position == p)
                    anchors[^1] = anchors[^1] with { Red = true }; // a repeated point = a sharp corner
                else
                    anchors.Add(new SliderAnchor(p));
            }
            return anchors;
        }

        /// <summary>Expands logical anchors back to a raw point list, doubling interior red anchors.</summary>
        private static List<Vector2> Expand(IReadOnlyList<SliderAnchor> anchors)
        {
            var raw = new List<Vector2>(anchors.Count + 2);
            for (int i = 0; i < anchors.Count; i++)
            {
                raw.Add(anchors[i].Position);
                // A red corner is two coincident points; meaningless on the head or tail.
                if (anchors[i].Red && i > 0 && i < anchors.Count - 1)
                    raw.Add(anchors[i].Position);
            }
            return raw;
        }

        /// <summary>The <c>type|x:y|...</c> curve field (anchors after the head; interior reds doubled).</summary>
        public static string CurveField(char type, IReadOnlyList<SliderAnchor> anchors)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(type);
            for (int i = 1; i < anchors.Count; i++)
            {
                int x = (int)Math.Round(anchors[i].X);
                int y = (int)Math.Round(anchors[i].Y);
                sb.Append('|').Append(x).Append(':').Append(y);
                if (anchors[i].Red && i < anchors.Count - 1)
                    sb.Append('|').Append(x).Append(':').Append(y);
            }
            return sb.ToString();
        }

        /// <summary>Computes the slider polyline for these anchors, truncated to <paramref name="pixelLength"/> (0 = full).</summary>
        public static List<Vector2> ComputePath(IReadOnlyList<SliderAnchor> anchors, char type, double pixelLength = 0) =>
            new List<Vector2>(SliderPathCalculator.Calculate(Expand(anchors), type, pixelLength));

        public static double PathLength(IReadOnlyList<Vector2> path)
        {
            double len = 0;
            for (int i = 1; i < path.Count; i++)
                len += (path[i] - path[i - 1]).Length;
            return len;
        }

        /// <summary>
        /// Picks a valid curve type for the given anchors, preserving <paramref name="current"/> where it still
        /// applies: any red corner forces Bézier; a Perfect curve must have exactly three anchors.
        /// </summary>
        public static char AdjustType(char current, IReadOnlyList<SliderAnchor> anchors)
        {
            if (anchors.Any(a => a.Red))
                return 'B';
            if (current == 'P' && anchors.Count != 3)
                return 'B';
            return current;
        }
    }
}
