using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small circular dim dial shown at the bottom-left of the editor, above the timeline. The song
    /// background is always shown; scrolling over this wheel adjusts how much it is dimmed - represented as
    /// a ring that progressively encircles the button (full ring = 100% dim / black, empty = 0% dim). The
    /// centre shows the current dim percentage.
    /// </summary>
    public partial class BackgroundToggleButton : CircularContainer, IHasTooltip
    {
        private readonly BindableFloat dim;
        private readonly bool songAvailable;

        private CircularProgress dimRing = null!;
        private Box fill = null!;
        private SpriteText label = null!;

        public LocalisableString TooltipText => "Scroll to change background dim";

        public BackgroundToggleButton(BindableFloat dim, bool songAvailable)
        {
            this.dim = dim;
            this.songAvailable = songAvailable;

            Size = new Vector2(30);
            // The dial only does anything while there's a background to dim.
            Alpha = songAvailable ? 1f : 0f;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                // The dim ring sits behind the inner circle; only the part not covered shows as a border.
                dimRing = new CircularProgress
                {
                    RelativeSizeAxes = Axes.Both,
                    InnerRadius = 1f,
                    Colour = OsuColour.Pink,
                },
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Scale = new Vector2(0.78f),
                    Masking = true,
                    Children = new Drawable[]
                    {
                        fill = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundRaised },
                        label = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 9, weight: "Bold"),
                        },
                    },
                },
            };

            dim.BindValueChanged(_ => updateRing(), true);
        }

        private void updateRing()
        {
            dimRing.Progress = Math.Clamp(dim.Value, 0f, 1f);
            label.Text = $"{Math.Round(dim.Value * 100)}";
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            if (!songAvailable)
                return false;

            // Inverted: scrolling up brightens (less dim), scrolling down darkens (more dim).
            dim.Value = Math.Clamp(dim.Value - e.ScrollDelta.Y * 0.05f, 0f, 1f);
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            fill.FadeColour(OsuColour.BackgroundDark, 120);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            fill.FadeColour(OsuColour.BackgroundRaised, 120);
            base.OnHoverLost(e);
        }
    }
}
