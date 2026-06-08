using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A flat, rounded button styled after osu!lazer, with animated hover and press colour feedback
    /// driven entirely by the framework's Transform API. No hover scaling.
    /// </summary>
    public partial class OsuButton : ClickableContainer
    {
        private readonly string text;
        private readonly Color4 accent;

        private Box background = null!;
        private Box hoverGlow = null!;

        /// <summary>Font size of the button label.</summary>
        public float FontSize { get; init; } = 24;

        /// <param name="text">Label rendered in the centre of the button.</param>
        /// <param name="accent">Accent colour used for the hover/press highlight.</param>
        public OsuButton(string text, Color4 accent)
        {
            this.text = text;
            this.accent = accent;

            // Rounded corners; children are clipped to the button bounds.
            Masking = true;
            CornerRadius = 8;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Surface,
                },
                hoverGlow = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = accent,
                    Alpha = 0,
                },
                new SpriteText
                {
                    Text = text,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: FontSize, weight: "SemiBold"),
                },
            };

            // Dim the button while it is disabled (ClickableContainer suppresses the Action automatically).
            Enabled.BindValueChanged(e => this.FadeTo(e.NewValue ? 1f : 0.5f, 150, Easing.OutQuint), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverGlow.FadeTo(0.25f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverGlow.FadeOut(200, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            background.FlashColour(accent, 200, Easing.OutQuint);
            return base.OnMouseDown(e);
        }
    }
}
