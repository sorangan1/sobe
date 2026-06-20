using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The Auto-preview "tapping" indicator, mirroring osu!lazer's key overlay: two stacked keys (K1/K2) that
    /// light up as the Auto cursor hits objects (alternating left/right, like a real player), each counting its
    /// presses. Purely visual; driven by the playfield from the current playback time.
    /// </summary>
    public partial class KeyOverlay : CompositeDrawable
    {
        private readonly KeyButton k1;
        private readonly KeyButton k2;

        public KeyOverlay()
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    k1 = new KeyButton("K1"),
                    k2 = new KeyButton("K2"),
                },
            };
        }

        /// <summary>Registers a fresh press on the given key (0 = K1, 1 = K2): bumps its count and flashes it.</summary>
        public void Press(int key) => (key == 0 ? k1 : k2).Press();

        /// <summary>Lights the held key (0 = K1, 1 = K2, -1 = none).</summary>
        public void SetHeld(int key)
        {
            k1.SetHeld(key == 0);
            k2.SetHeld(key == 1);
        }

        /// <summary>Clears all key lighting and resets the press counts (e.g. when the preview is turned off).</summary>
        public void Reset()
        {
            k1.Reset();
            k2.Reset();
        }

        private partial class KeyButton : CompositeDrawable
        {
            private readonly SpriteText label;
            private readonly SpriteText countText;
            private readonly Box fill;
            private int count;

            public KeyButton(string name)
            {
                Size = new Vector2(48, 40);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
                BorderThickness = 2;
                BorderColour = EditorTheme.Colours.Info;

                InternalChildren = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken, Alpha = 0.65f },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            label = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = name,
                                Colour = EditorTheme.Colours.Text,
                                Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                            },
                            countText = new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "0",
                                Colour = EditorTheme.Colours.TextMuted,
                                Font = FontUsage.Default.With(size: 11, weight: "SemiBold"),
                            },
                        },
                    },
                };
            }

            public void Press()
            {
                count++;
                countText.Text = count.ToString();
            }

            public void SetHeld(bool held)
            {
                fill.FadeColour(held ? EditorTheme.Colours.Info : EditorTheme.Colours.Sunken, 60);
                fill.FadeTo(held ? 0.95f : 0.65f, 60);
                label.FadeColour(held ? OsuColour.BackgroundDark : EditorTheme.Colours.Text, 60);
            }

            public void Reset()
            {
                count = 0;
                countText.Text = "0";
                SetHeld(false);
            }
        }
    }
}
