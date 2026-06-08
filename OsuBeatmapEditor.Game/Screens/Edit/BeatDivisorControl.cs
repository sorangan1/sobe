using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A compact control for the shared beat-snap divisor: "- 1/N +" with buttons, and scroll-to-change.
    /// </summary>
    public partial class BeatDivisorControl : CompositeDrawable
    {
        [Resolved]
        private BeatSnapDivisor beatSnap { get; set; } = null!;

        private SpriteText label = null!;

        public BeatDivisorControl()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.6f },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4, 0),
                    Padding = new MarginPadding(4),
                    Children = new Drawable[]
                    {
                        new OsuButton("-", OsuColour.Surface) { Size = new Vector2(24), FontSize = 18, CornerRadius = 4, Action = () => beatSnap.Step(-1) },
                        new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Width = 46,
                            Height = 24,
                            Child = label = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Colour = OsuColour.Text,
                                Font = FontUsage.Default.With(size: 17, weight: "Bold"),
                            },
                        },
                        new OsuButton("+", OsuColour.Surface) { Size = new Vector2(24), FontSize = 18, CornerRadius = 4, Action = () => beatSnap.Step(1) },
                    },
                },
            };

            beatSnap.Value.BindValueChanged(v => label.Text = $"1/{v.NewValue}", true);
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            beatSnap.Step(e.ScrollDelta.Y > 0 ? 1 : -1);
            return true;
        }
    }
}
