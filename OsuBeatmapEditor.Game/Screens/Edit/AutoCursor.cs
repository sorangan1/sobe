using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A non-interactive "Auto" cursor that follows the beatmap exactly like osu!lazer's Auto mod: it rests on
    /// each hit object as it is hit, traces sliders along their path, spins on spinners, and glides between
    /// objects across the gaps. Purely a visual preview - it never touches the saved map. Lives inside the
    /// playfield's osu!pixel coordinate space (a child of the play area), so positions are plain osu!pixels.
    /// Cursor colour and trail length are configurable. <see cref="PositionSource"/> supplies the position per
    /// frame (null hides the cursor - disabled, or no objects).
    /// </summary>
    public partial class AutoCursor : CompositeDrawable
    {
        /// <summary>The cursor's current osu!pixel position, or null to hide it.</summary>
        public Func<Vector2?>? PositionSource;

        private const float cursor_size = 22f; // osu!pixels
        private const int max_trail = 120;

        private readonly Container trailContainer;
        private readonly CircularContainer head;
        private readonly Box headFill;

        private readonly List<CircularContainer> trailParts = new List<CircularContainer>();
        private readonly List<Box> trailFills = new List<Box>();

        // Most-recent-first ring of recent positions; the trail draws the first `trailLength` of these.
        private readonly LinkedList<Vector2> history = new LinkedList<Vector2>();

        private Color4 cursorColour = new Color4(1f, 0.86f, 0.2f, 1f); // yellow default
        private int trailLength = 10;
        private float trailWidth = 1f;

        public AutoCursor()
        {
            // Fill the play area exactly (512x384) so child positions are osu!pixels with no offset.
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                trailContainer = new Container { RelativeSizeAxes = Axes.Both },
                head = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(cursor_size),
                    Masking = true,
                    BorderThickness = 2.5f,
                    BorderColour = Color4.White,
                    Alpha = 0,
                    Child = headFill = new Box { RelativeSizeAxes = Axes.Both },
                },
            };

            for (int i = 0; i < max_trail; i++)
            {
                var fill = new Box { RelativeSizeAxes = Axes.Both };
                var part = new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Size = new Vector2(cursor_size * 0.72f),
                    Masking = true,
                    Alpha = 0,
                    Child = fill,
                };
                trailParts.Add(part);
                trailFills.Add(fill);
                trailContainer.Add(part);
            }

            applyColour();
        }

        /// <summary>Sets the cursor + trail colour.</summary>
        public void SetColour(Color4 colour)
        {
            cursorColour = colour;
            applyColour();
        }

        /// <summary>Sets how many trailing segments follow the cursor (0 = none).</summary>
        public void SetTrailLength(int length) => trailLength = Math.Clamp(length, 0, max_trail);

        /// <summary>Sets the trail thickness multiplier (1 = default).</summary>
        public void SetTrailWidth(float width) => trailWidth = Math.Clamp(width, 0.2f, 4f);

        private void applyColour()
        {
            headFill.Colour = cursorColour;
            foreach (var fill in trailFills)
                fill.Colour = cursorColour;
        }

        protected override void Update()
        {
            base.Update();

            Vector2? pos = PositionSource?.Invoke();

            if (pos == null)
            {
                head.Alpha = 0;
                history.Clear();
                foreach (var part in trailParts)
                    part.Alpha = 0;
                return;
            }

            head.Alpha = 1;
            head.Position = pos.Value;

            history.AddFirst(pos.Value);
            while (history.Count > Math.Max(1, trailLength))
                history.RemoveLast();

            int shown = Math.Min(trailLength, trailParts.Count);

            int i = 0;
            var node = history.First;
            for (; i < shown && node != null; i++, node = node.Next)
            {
                float t = (float)i / Math.Max(1, trailLength); // 0 (nearest head) → ~1 (tail)
                var part = trailParts[i];
                part.Position = node.Value;
                part.Alpha = (1f - t) * 0.5f;
                part.Scale = new Vector2((1f - t * 0.45f) * trailWidth);
            }

            // Hide every remaining part (covers a shrunk trail, so nothing lingers at its last position).
            for (; i < trailParts.Count; i++)
                trailParts[i].Alpha = 0;
        }
    }
}
