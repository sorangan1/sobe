using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using osu.Framework.Utils;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Slider path geometry, ported from osu!lazer so behaviour/rendering match exactly:
    /// <list type="bullet">
    /// <item><see cref="ParseControlPoints"/> mirrors <c>ConvertHitObjectParser.convertPathString</c>/<c>convertPoints</c>
    /// (inline type letters + implicit segment splitting at duplicate points).</item>
    /// <item><see cref="ComputePath"/> mirrors <c>SliderPath.calculatePath</c>/<c>calculateSubPath</c>/<c>calculateLength</c>
    /// (segment by typed control point, per-segment spline via the framework's <see cref="PathApproximator"/>, then
    /// trim/extend to the expected distance).</item>
    /// <item><see cref="CurveField"/> mirrors <c>LegacyBeatmapEncoder.addPathData</c> (inline type letters).</item>
    /// </list>
    /// Control points are stored in <b>absolute</b> osu!pixels (lazer keeps them relative to the head); the
    /// algorithms are translation-invariant, so results are identical.
    /// </summary>
    public static class SliderGeometry
    {
        private const int first_lazer_version = 128;

        // --- Parsing (.osu curve field -> typed control points) ---

        /// <summary>Parses a raw slider line's head + curve field into absolute, typed control points.</summary>
        public static List<SliderControlPoint> ParseControlPoints(string rawLine, int formatVersion = 14)
        {
            string[] parts = rawLine.Split(',');
            if (parts.Length < 6)
                return new List<SliderControlPoint>();

            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float hx);
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hy);
            var head = new Vector2(hx, hy);

            return convertPathString(parts[5], head, formatVersion);
        }

        private static List<SliderControlPoint> convertPathString(string pointString, Vector2 head, int formatVersion)
        {
            string[] tokens = pointString.Split('|');

            var allPoints = new List<Vector2>(tokens.Length);
            var segments = new List<(SliderPathType type, int startIndex)>();

            foreach (string s in tokens)
            {
                if (s.Length == 0)
                    continue;

                if (char.IsLetter(s[0]))
                {
                    segments.Add((convertPathType(s), allPoints.Count));

                    // The first segment is prepended by the head point (relative-zero in lazer; absolute head here).
                    if (allPoints.Count == 0)
                        allPoints.Add(head);
                }
                else
                {
                    allPoints.Add(readPoint(s));
                }
            }

            var result = new List<SliderControlPoint>();

            for (int i = 0; i < segments.Count; i++)
            {
                int startIndex = segments[i].startIndex;
                if (i < segments.Count - 1)
                {
                    int endIndex = segments[i + 1].startIndex;
                    result.AddRange(convertPoints(segments[i].type, allPoints.GetRange(startIndex, endIndex - startIndex), allPoints[endIndex], formatVersion));
                }
                else
                {
                    result.AddRange(convertPoints(segments[i].type, allPoints.GetRange(startIndex, allPoints.Count - startIndex), null, formatVersion));
                }
            }

            return result;
        }

        private static Vector2 readPoint(string value)
        {
            string[] xy = value.Split(':');
            float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
            float.TryParse(xy.Length > 1 ? xy[1] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
            return new Vector2(x, y);
        }

        /// <summary>Port of lazer's <c>convertPoints</c>: splits a point run into typed segments (implicit splits at duplicate points).</summary>
        private static IEnumerable<SliderControlPoint> convertPoints(SliderPathType type, List<Vector2> points, Vector2? endPoint, int formatVersion)
        {
            var vertices = new SliderControlPoint[points.Count];
            for (int i = 0; i < points.Count; i++)
                vertices[i] = new SliderControlPoint(points[i]);

            // Perfect-curve edge cases (match stable / lazer).
            if (type.Type == SliderSplineType.PerfectCurve)
            {
                int endPointLength = endPoint == null ? 0 : 1;

                if (formatVersion < first_lazer_version)
                {
                    if (vertices.Length + endPointLength != 3)
                        type = SliderPathType.Bezier;
                    else if (isLinear(points[0], points[1], endPoint ?? points[2]))
                        type = SliderPathType.Linear;
                }
                else if (vertices.Length + endPointLength > 3)
                {
                    type = SliderPathType.Bezier;
                }
            }

            // The first control point must have a definite type.
            vertices[0] = vertices[0] with { Type = type };

            int startIndex = 0;
            int endIndex = 0;

            while (++endIndex < vertices.Length)
            {
                if (vertices[endIndex].Position != vertices[endIndex - 1].Position)
                    continue;

                // Legacy Catmull sliders treat adjacent segments as one.
                if (type.Type == SliderSplineType.Catmull && endIndex > 1 && formatVersion < first_lazer_version)
                    continue;

                if (endIndex == vertices.Length - 1)
                    continue;

                vertices[endIndex - 1] = vertices[endIndex - 1] with { Type = type };

                for (int j = startIndex; j < endIndex; j++)
                    yield return vertices[j];

                startIndex = endIndex + 1;
            }

            if (startIndex < endIndex)
            {
                for (int j = startIndex; j < endIndex; j++)
                    yield return vertices[j];
            }
        }

        private static bool isLinear(Vector2 p0, Vector2 p1, Vector2 p2) =>
            Precision.AlmostEquals(0, (p1.Y - p0.Y) * (p2.X - p0.X) - (p1.X - p0.X) * (p2.Y - p0.Y));

        private static SliderPathType convertPathType(string input)
        {
            switch (input[0])
            {
                default:
                case 'C':
                    return SliderPathType.Catmull;

                case 'B':
                    if (input.Length > 1 && int.TryParse(input.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int degree) && degree > 0)
                        return new SliderPathType(SliderSplineType.BSpline, degree);

                    return SliderPathType.Bezier;

                case 'L':
                    return SliderPathType.Linear;

                case 'P':
                    return SliderPathType.PerfectCurve;
            }
        }

        // --- Path computation (typed control points -> polyline) ---

        /// <summary>
        /// Computes the slider polyline (absolute osu!pixels). Port of lazer's <c>calculatePath</c> + <c>calculateLength</c>:
        /// builds each segment with its spline, then trims/extends the tail to <paramref name="expectedDistance"/>
        /// (0 or null = the curve's natural length).
        /// </summary>
        public static List<Vector2> ComputePath(IReadOnlyList<SliderControlPoint> controlPoints, double? expectedDistance = null)
        {
            var calculatedPath = new List<Vector2>();
            if (controlPoints.Count == 0)
                return calculatedPath;

            var vertices = new Vector2[controlPoints.Count];
            for (int i = 0; i < controlPoints.Count; i++)
                vertices[i] = controlPoints[i].Position;

            int start = 0;

            for (int i = 0; i < controlPoints.Count; i++)
            {
                if (controlPoints[i].Type == null && i < controlPoints.Count - 1)
                    continue;

                var segmentVertices = new ReadOnlySpan<Vector2>(vertices, start, i - start + 1);
                var segmentType = controlPoints[start].Type ?? SliderPathType.Linear;

                if (segmentVertices.Length == 1)
                {
                    calculatedPath.Add(segmentVertices[0]);
                }
                else if (segmentVertices.Length > 1)
                {
                    List<Vector2> subPath = calculateSubPath(segmentVertices, segmentType);

                    bool skipFirst = calculatedPath.Count > 0 && subPath.Count > 0 && calculatedPath[^1] == subPath[0];

                    for (int j = skipFirst ? 1 : 0; j < subPath.Count; j++)
                        calculatedPath.Add(subPath[j]);
                }

                start = i;
            }

            if (expectedDistance is double dist && dist > 0)
                applyExpectedDistance(calculatedPath, dist);

            return calculatedPath;
        }

        private static List<Vector2> calculateSubPath(ReadOnlySpan<Vector2> subControlPoints, SliderPathType type)
        {
            switch (type.Type)
            {
                case SliderSplineType.Linear:
                    return PathApproximator.LinearToPiecewiseLinear(subControlPoints);

                case SliderSplineType.PerfectCurve:
                {
                    if (subControlPoints.Length != 3)
                        break;

                    var props = new CircularArcProperties(subControlPoints);
                    if (!props.IsValid)
                        break;

                    int subPoints = 2f * props.Radius <= 0.1f
                        ? 2
                        : Math.Max(2, (int)Math.Ceiling(props.ThetaRange / (2.0 * Math.Acos(1f - 0.1f / props.Radius))));

                    if (subPoints >= 1000)
                        break;

                    List<Vector2> subPath = PathApproximator.CircularArcToPiecewiseLinear(subControlPoints);
                    if (subPath.Count == 0)
                        break;

                    return subPath;
                }

                case SliderSplineType.Catmull:
                    return PathApproximator.CatmullToPiecewiseLinear(subControlPoints);
            }

            return PathApproximator.BSplineToPiecewiseLinear(subControlPoints, type.Degree ?? subControlPoints.Length);
        }

        /// <summary>Port of the length step of lazer's <c>calculateLength</c>: trims or extends the tail to the expected distance.</summary>
        private static void applyExpectedDistance(List<Vector2> path, double expectedDistance)
        {
            if (path.Count < 2)
                return;

            var cumulative = new List<double> { 0 };
            double length = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                length += (path[i + 1] - path[i]).Length;
                cumulative.Add(length);
            }

            if (length == expectedDistance)
                return;

            // osu-stable: if the last two points coincide, never extend.
            if (path[^1] == path[^2] && expectedDistance > length)
                return;

            cumulative.RemoveAt(cumulative.Count - 1);
            int pathEndIndex = path.Count - 1;

            if (length > expectedDistance)
            {
                while (cumulative.Count > 0 && cumulative[^1] >= expectedDistance)
                {
                    cumulative.RemoveAt(cumulative.Count - 1);
                    path.RemoveAt(pathEndIndex--);
                }
            }

            if (pathEndIndex <= 0)
                return;

            Vector2 dir = (path[pathEndIndex] - path[pathEndIndex - 1]).Normalized();
            path[pathEndIndex] = path[pathEndIndex - 1] + dir * (float)(expectedDistance - cumulative[^1]);
        }

        /// <summary>
        /// Assigns each segment-start control point a spline type from its segment's length, as lazer's slider
        /// placement does: a 2-point segment is Linear, 3-point is a Perfect curve, longer is Bézier. Segment
        /// boundaries are the head plus any control point that already carries a (placeholder) type.
        /// </summary>
        public static List<SliderControlPoint> InferSegmentTypes(IReadOnlyList<SliderControlPoint> points)
        {
            var result = new List<SliderControlPoint>(points);

            var bounds = new List<int>();
            for (int i = 0; i < result.Count; i++)
            {
                if (i == 0 || result[i].Type != null)
                    bounds.Add(i);
            }

            for (int b = 0; b < bounds.Count; b++)
            {
                int start = bounds[b];
                int next = b + 1 < bounds.Count ? bounds[b + 1] : -1;
                int segLen = next >= 0 ? next - start + 1 : result.Count - start;

                SliderPathType t = segLen == 2 ? SliderPathType.Linear
                    : segLen == 3 ? SliderPathType.PerfectCurve
                    : SliderPathType.Bezier;

                result[start] = result[start] with { Type = t };
            }

            return result;
        }

        public static double PathLength(IReadOnlyList<Vector2> path)
        {
            double len = 0;
            for (int i = 1; i < path.Count; i++)
                len += (path[i] - path[i - 1]).Length;
            return len;
        }

        // --- Encoding (typed control points -> .osu curve field) ---

        /// <summary>Port of lazer's <c>addPathData</c>: emits the curve field with inline per-segment type letters.</summary>
        public static string CurveField(IReadOnlyList<SliderControlPoint> controlPoints)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < controlPoints.Count; i++)
            {
                var point = controlPoints[i];

                if (point.Type is SliderPathType type)
                {
                    switch (type.Type)
                    {
                        case SliderSplineType.BSpline:
                            sb.Append(type.Degree > 0 ? $"B{type.Degree}|" : "B|");
                            break;

                        case SliderSplineType.Catmull:
                            sb.Append("C|");
                            break;

                        case SliderSplineType.PerfectCurve:
                            sb.Append("P|");
                            break;

                        case SliderSplineType.Linear:
                            sb.Append("L|");
                            break;
                    }
                }

                if (i != 0)
                {
                    sb.Append(FormattableString.Invariant($"{(int)Math.Round(point.X)}:{(int)Math.Round(point.Y)}"));
                    if (i != controlPoints.Count - 1)
                        sb.Append('|');
                }
            }

            return sb.ToString();
        }
    }
}
