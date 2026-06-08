using System;
using osu.Framework.Bindables;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The editor's shared beat-snap divisor (1/N). Cached so the timeline grid, object snapping and
    /// scrolling all snap to the same resolution, mirroring osu!lazer's beat-divisor control.
    /// </summary>
    public class BeatSnapDivisor
    {
        /// <summary>The snap denominators the control cycles through.</summary>
        public static readonly int[] Available = { 1, 2, 3, 4, 6, 8, 12, 16 };

        public readonly BindableInt Value = new BindableInt(4) { MinValue = 1, MaxValue = 16 };

        /// <summary>Moves to a coarser (-1) or finer (+1) divisor in the available list.</summary>
        public void Step(int direction)
        {
            int index = Array.IndexOf(Available, Value.Value);
            if (index < 0)
                index = Array.IndexOf(Available, 4);

            Value.Value = Available[Math.Clamp(index + direction, 0, Available.Length - 1)];
        }
    }
}
