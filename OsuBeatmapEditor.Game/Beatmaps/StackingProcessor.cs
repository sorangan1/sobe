using System;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Computes osu! Standard "stacking": objects placed close together in both time and position are
    /// nudged diagonally so they don't perfectly overlap. The result is each object's stack height; the
    /// playfield turns that into a visual offset. Follows osu!'s standard stacking behaviour.
    /// </summary>
    public static class StackingProcessor
    {
        // Two positions count as "the same place" within this many osu!pixels.
        private const float stack_distance = 3f;

        /// <summary>
        /// Recomputes <see cref="HitObjectModel.StackHeight"/> for every object in place.
        /// <paramref name="preempt"/> is the AR preempt (ms); <paramref name="stackLeniency"/> the map setting.
        /// Objects are assumed sorted by start time.
        /// </summary>
        public static void Apply(System.Collections.Generic.List<HitObjectModel> objects, double preempt, float stackLeniency)
        {
            int count = objects.Count;
            if (count < 2)
                return;

            var stack = new int[count];
            double stackThreshold = preempt * stackLeniency;

            // Walk backwards; for each object, look back at earlier objects within the time window and
            // raise/lower their stack heights when they share a position.
            for (int i = count - 1; i > 0; i--)
            {
                int n = i;
                var objectI = objects[i];

                // Already stacked, or spinners (which never stack) - skip.
                if (stack[i] != 0 || objectI.Kind == HitObjectKind.Spinner)
                    continue;

                if (objectI.Kind == HitObjectKind.Circle)
                {
                    while (--n >= 0)
                    {
                        var objectN = objects[n];
                        if (objectN.Kind == HitObjectKind.Spinner)
                            continue;

                        if (objectI.StartTime - endTime(objectN) > stackThreshold)
                            break;

                        // A slider tail under this circle pulls the whole intervening run down onto the tail.
                        if (objectN.Kind == HitObjectKind.Slider && distance(endPosition(objectN), position(objectI)) < stack_distance)
                        {
                            int offset = stack[i] - stack[n] + 1;
                            for (int j = n + 1; j <= i; j++)
                            {
                                if (distance(endPosition(objectN), position(objects[j])) < stack_distance)
                                    stack[j] -= offset;
                            }

                            break;
                        }

                        if (distance(position(objectN), position(objectI)) < stack_distance)
                        {
                            stack[n] = stack[i] + 1;
                            objectI = objectN;
                            i = n;
                        }
                    }
                }
                else if (objectI.Kind == HitObjectKind.Slider)
                {
                    while (--n >= 0)
                    {
                        var objectN = objects[n];
                        if (objectN.Kind == HitObjectKind.Spinner)
                            continue;

                        if (objectI.StartTime - objectN.StartTime > stackThreshold)
                            break;

                        if (distance(endPosition(objectN), position(objectI)) < stack_distance)
                        {
                            stack[n] = stack[i] + 1;
                            objectI = objectN;
                            i = n;
                        }
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (objects[i].StackHeight != stack[i])
                    objects[i] = objects[i] with { StackHeight = stack[i] };
            }
        }

        private static double endTime(HitObjectModel o) => o.StartTime + (o.Kind == HitObjectKind.Slider ? o.Duration : 0);

        private static Vector2 position(HitObjectModel o) =>
            o.Path is { Count: > 0 } path ? path[0] : new Vector2(o.X, o.Y);

        private static Vector2 endPosition(HitObjectModel o)
        {
            if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } path)
                return o.Slides % 2 == 1 ? path[^1] : path[0];

            return new Vector2(o.X, o.Y);
        }

        private static float distance(Vector2 a, Vector2 b) => (a - b).Length;
    }
}
