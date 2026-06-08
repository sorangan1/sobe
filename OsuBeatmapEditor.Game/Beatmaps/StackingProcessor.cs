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
        /// Recomputes <see cref="HitObjectModel.StackHeight"/> for every object in place. A direct port of
        /// osu!lazer's <c>OsuBeatmapProcessor.applyStacking</c> (modern, beatmap version &gt;= 6) for the
        /// full object range, so only the single reverse pass runs (lazer skips the forward extend when the
        /// end index is the last object). <paramref name="preempt"/> is the AR preempt (ms), truncated to an
        /// integer to match stable; <paramref name="stackLeniency"/> the map setting. Objects must be sorted by time.
        /// </summary>
        public static void Apply(System.Collections.Generic.List<HitObjectModel> objects, double preempt, float stackLeniency)
        {
            int count = objects.Count;
            if (count < 2)
                return;

            var stack = new int[count];

            // Truncate to int and keep the result as float, matching stable (see lazer's calculateStackThreshold).
            float stackThreshold = (int)preempt * stackLeniency;

            // Reverse pass: walk backwards, building each stack chain from its top object downward.
            for (int i = count - 1; i > 0; i--)
            {
                if (stack[i] != 0 || objects[i].Kind == HitObjectKind.Spinner)
                    continue;

                int n = i;
                int cur = i; // the chain's current object (lazer's reassigned `objectI`); the outer `i` is never moved.

                if (objects[i].Kind == HitObjectKind.Circle)
                {
                    while (--n >= 0)
                    {
                        var objectN = objects[n];
                        if (objectN.Kind == HitObjectKind.Spinner)
                            continue;

                        // Integer truncation of both times is required to match stable.
                        if ((int)objects[cur].StartTime - (int)endTime(objectN) > stackThreshold)
                            break;

                        // A slider tail under this stack pulls the whole intervening run down (negative stacking).
                        if (objectN.Kind == HitObjectKind.Slider && distance(endPosition(objectN), position(objects[cur])) < stack_distance)
                        {
                            int offset = stack[cur] - stack[n] + 1;
                            for (int j = n + 1; j <= i; j++)
                            {
                                if (distance(endPosition(objectN), position(objects[j])) < stack_distance)
                                    stack[j] -= offset;
                            }

                            break;
                        }

                        if (distance(position(objectN), position(objects[cur])) < stack_distance)
                        {
                            stack[n] = stack[cur] + 1;
                            cur = n;
                        }
                    }
                }
                else if (objects[i].Kind == HitObjectKind.Slider)
                {
                    while (--n >= 0)
                    {
                        var objectN = objects[n];
                        if (objectN.Kind == HitObjectKind.Spinner)
                            continue;

                        if (objects[cur].StartTime - objectN.StartTime > stackThreshold)
                            break;

                        if (distance(endPosition(objectN), position(objects[cur])) < stack_distance)
                        {
                            stack[n] = stack[cur] + 1;
                            cur = n;
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

        // Stacking uses the slider's geometric path end (EndPosition = Position + Path.PositionAt(1)),
        // independent of repeat count - matching lazer (whose stacking ignores repeats).
        private static Vector2 endPosition(HitObjectModel o) =>
            o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } path ? path[^1] : new Vector2(o.X, o.Y);

        private static float distance(Vector2 a, Vector2 b) => (a - b).Length;
    }
}
