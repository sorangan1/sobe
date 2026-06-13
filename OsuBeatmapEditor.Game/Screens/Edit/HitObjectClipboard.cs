using System.Collections.Generic;
using System.Linq;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A process-wide clipboard of copied hit objects, so a copy in one map/difficulty can be pasted into
    /// another. Holds plain <see cref="HitObjectModel"/> values (absolute osu!pixel positions, raw .osu lines);
    /// the pasting editor re-times them to its own playhead and re-derives slider durations/combos for its
    /// own timing. Static so it survives the per-map editor screen being recreated.
    /// </summary>
    public static class HitObjectClipboard
    {
        private static readonly List<HitObjectModel> objects = new List<HitObjectModel>();

        public static bool HasContent => objects.Count > 0;

        /// <summary>The copied objects, in start-time order.</summary>
        public static IReadOnlyList<HitObjectModel> Objects => objects;

        /// <summary>Replaces the clipboard contents with the given objects (stored time-ordered).</summary>
        public static void Set(IEnumerable<HitObjectModel> source)
        {
            objects.Clear();
            objects.AddRange(source.OrderBy(o => o.StartTime));
        }
    }
}
