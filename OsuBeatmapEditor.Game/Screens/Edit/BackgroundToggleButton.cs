using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Small circular button shown at the bottom-left of the editor, above the timeline. Clicking it
    /// toggles between the song's background image and the custom editor colour. While the song
    /// background is active, scrolling over the button adjusts its dim - represented as a ring that
    /// progressively encircles the button (full ring = 100% dim / black, empty = 0% dim).
    /// </summary>
    public partial class BackgroundToggleButton : CircularContainer
    {
        private readonly BindableBool useSong;
        private readonly BindableFloat dim;
        private readonly bool songAvailable;

        private CircularProgress dimRing = null!;
        private Box fill = null!;
        private SpriteText icon = null!;

        public BackgroundToggleButton(BindableBool useSong, BindableFloat dim, bool songAvailable)
        {
            this.useSong = useSong;
            this.dim = dim;
            this.songAvailable = songAvailable;

            Size = new Vector2(30);
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
                        fill = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface },
                        icon = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                        },
                    },
                },
            };

            useSong.BindValueChanged(_ => updateState(), true);
            dim.BindValueChanged(_ => updateRing(), true);
        }

        private void updateState()
        {
            bool song = useSong.Value && songAvailable;
            icon.Text = song ? "~" : "#";
            fill.Colour = song ? OsuColour.BackgroundRaised : OsuColour.Surface;
            // The dim ring is only meaningful while the song background is shown.
            dimRing.FadeTo(song ? 1f : 0f, 120);
        }

        private void updateRing() => dimRing.Progress = Math.Clamp(dim.Value, 0f, 1f);

        protected override bool OnClick(ClickEvent e)
        {
            if (!songAvailable)
                return false;

            useSong.Value = !useSong.Value;
            return true;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            // Only adjust dim while the real song background is active.
            if (!songAvailable || !useSong.Value)
                return false;

            dim.Value = Math.Clamp(dim.Value + e.ScrollDelta.Y * 0.05f, 0f, 1f);
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            fill.FadeColour(OsuColour.BackgroundDark, 120);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateState();
            base.OnHoverLost(e);
        }
    }
}
