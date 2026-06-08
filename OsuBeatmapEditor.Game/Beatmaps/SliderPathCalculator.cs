using System;
using System.Collections.Generic;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Computes a polyline approximation of an osu! slider path from its control points and curve type.
    /// Supports Linear (L), Bézier (B) and Perfect-circle (P); Catmull (C) falls back to linear.
    /// The result is truncated to the slider's pixel length, matching how osu! draws the body.
    /// </summary>
    public static class SliderPathCalculator
    {
        private const float bezier_tolerance = 0.25f;

        /// <summary>
        /// The curve type osu! infers from a freshly-traced anchor count, matching the editor's defaults:
        /// 2 points = Linear, 3 = Perfect circle (falls back to linear if collinear), 4+ = Bézier.
        /// </summary>
        public static char DefaultCurveType(int pointCount) => pointCount switch
        {
            <= 2 => 'L',
            3 => 'P',
            _ => 'B',
        };

        public static IReadOnlyList<Vector2> Calculate(IReadOnlyList<Vector2> controlPoints, char curveType, double pixelLength)
        {
            if (controlPoints.Count == 0)
                return controlPoints;

            List<Vector2> raw = curveType switch
            {
                'L' => new List<Vector2>(controlPoints),
                'P' => perfectCircle(controlPoints),
                _ => bezier(controlPoints), // 'B' and 'C' (Catmull approximated as Bézier/linear).
            };

            return pixelLength > 0 ? truncate(raw, pixelLength) : raw;
        }

        // --- Bézier (piecewise, split at repeated control points) ---

        private static List<Vector2> bezier(IReadOnlyList<Vector2> points)
        {
            var output = new List<Vector2>();
            var segment = new List<Vector2>();

            for (int i = 0; i < points.Count; i++)
            {
                segment.Add(points[i]);

                bool last = i == points.Count - 1;
                bool breakHere = !last && points[i] == points[i + 1];

                if (last || breakHere)
                {
                    sampleBezierSegment(segment, output);
                    segment = new List<Vector2>();
                }
            }

            if (output.Count == 0)
                output.AddRange(points);

            return output;
        }

        private static void sampleBezierSegment(List<Vector2> control, List<Vector2> output)
        {
            if (control.Count == 1)
            {
                output.Add(control[0]);
                return;
            }

            if (control.Count == 2)
            {
                if (output.Count == 0) output.Add(control[0]);
                output.Add(control[1]);
                return;
            }

            // Step count scaled by the control polygon length for a reasonably smooth curve.
            float length = 0;
            for (int i = 1; i < control.Count; i++)
                length += (control[i] - control[i - 1]).Length;

            int steps = Math.Clamp((int)(length * bezier_tolerance), 8, 200);

            for (int s = 0; s <= steps; s++)
            {
                if (s == 0 && output.Count > 0)
                    continue; // avoid duplicating the joint between segments

                output.Add(deCasteljau(control, s / (float)steps));
            }
        }

        private static Vector2 deCasteljau(List<Vector2> control, float t)
        {
            Span<Vector2> p = stackalloc Vector2[control.Count];
            for (int i = 0; i < control.Count; i++)
                p[i] = control[i];

            for (int k = 1; k < control.Count; k++)
                for (int i = 0; i < control.Count - k; i++)
                    p[i] = p[i] * (1 - t) + p[i + 1] * t;

            return p[0];
        }

        // --- Perfect circle (arc through 3 points) ---

        private static List<Vector2> perfectCircle(IReadOnlyList<Vector2> points)
        {
            if (points.Count != 3)
                return bezier(points);

            Vector2 a = points[0], b = points[1], c = points[2];

            // Circumcentre via perpendicular-bisector intersection.
            float aSq = b.LengthSquared - a.LengthSquared;
            float bSq = c.LengthSquared - a.LengthSquared;

            Vector2 ab = b - a;
            Vector2 ac = c - a;

            float det = ab.X * ac.Y - ab.Y * ac.X;
            if (Math.Abs(det) < 0.001f)
                return new List<Vector2> { a, b, c }; // collinear -> straight

            Vector2 centre = new Vector2(
                (ac.Y * aSq - ab.Y * bSq) / (2 * det),
                (ab.X * bSq - ac.X * aSq) / (2 * det));

            float radius = (a - centre).Length;

            double angA = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            double angB = Math.Atan2(b.Y - centre.Y, b.X - centre.X);
            double angC = Math.Atan2(c.Y - centre.Y, c.X - centre.X);

            // Choose sweep direction so the arc passes through b.
            bool ccw = !isBetween(angA, angB, angC);
            double end = angC;
            if (ccw && end > angA) end -= 2 * Math.PI;
            if (!ccw && end < angA) end += 2 * Math.PI;

            int steps = Math.Clamp((int)(Math.Abs(end - angA) * radius * bezier_tolerance), 8, 200);
            var output = new List<Vector2>();
            for (int s = 0; s <= steps; s++)
            {
                double ang = angA + (end - angA) * (s / (double)steps);
                output.Add(centre + new Vector2((float)(Math.Cos(ang) * radius), (float)(Math.Sin(ang) * radius)));
            }

            return output;
        }

        private static bool isBetween(double a, double b, double c)
        {
            // True if angle b lies on the counter-clockwise arc from a to c.
            a = norm(a); b = norm(b); c = norm(c);
            if (a < c) return a <= b && b <= c;
            return b >= a || b <= c;
        }

        private static double norm(double a)
        {
            while (a < 0) a += 2 * Math.PI;
            while (a >= 2 * Math.PI) a -= 2 * Math.PI;
            return a;
        }

        // --- Length truncation ---

        private static List<Vector2> truncate(List<Vector2> path, double pixelLength)
        {
            if (path.Count < 2)
                return path;

            var output = new List<Vector2> { path[0] };
            double remaining = pixelLength;

            for (int i = 1; i < path.Count; i++)
            {
                Vector2 seg = path[i] - path[i - 1];
                float segLen = seg.Length;

                if (segLen <= 0)
                    continue;

                if (segLen < remaining)
                {
                    output.Add(path[i]);
                    remaining -= segLen;
                }
                else
                {
                    output.Add(path[i - 1] + seg * (float)(remaining / segLen));
                    return output;
                }
            }

            return output;
        }
    }
}
