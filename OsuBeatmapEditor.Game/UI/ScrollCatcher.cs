using System;
using osu.Framework.Graphics;
using osu.Framework.Input.Events;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// An invisible, full-screen layer that catches scroll events which weren't consumed by anything
    /// in front of it (e.g. scrolling over empty space). Placed behind the screen stack so the
    /// carousel still scrolls normally; used to drive global volume adjustment.
    /// </summary>
    public partial class ScrollCatcher : Drawable
    {
        public Action<float>? Scrolled;

        public ScrollCatcher()
        {
            RelativeSizeAxes = Axes.Both;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            // Only Shift+wheel adjusts volume; plain scroll is left for normal use.
            if (!e.ShiftPressed)
                return false;

            Scrolled?.Invoke(e.ScrollDelta.Y);
            return true;
        }
    }
}
