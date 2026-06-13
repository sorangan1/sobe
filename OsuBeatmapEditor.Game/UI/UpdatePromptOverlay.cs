using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// One-time prompt asking whether the user wants automatic updates. The choice is reported via
    /// <see cref="Chosen"/> (true = enable automatic updates, false = check manually).
    /// </summary>
    public partial class UpdatePromptOverlay : VisibilityContainer
    {
        /// <summary>Invoked once with the user's choice (true = automatic updates on).</summary>
        public Action<bool>? Chosen;

        private Container panel = null!;

        protected override bool StartHidden => true;

        public UpdatePromptOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = osuTK.Graphics.Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 480,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Automatic updates",
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Title(),
                                },
                                new TextFlowContainer(t => t.Font = EditorTheme.Type.Body())
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Text = "Should sobe download and install new versions automatically when it starts? "
                                           + "You can change this later in Settings.",
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Not now", OsuColour.Surface)
                                        {
                                            Size = new Vector2(140, 44),
                                            Action = () => choose(false),
                                        },
                                        new OsuButton("Enable", OsuColour.Pink)
                                        {
                                            Size = new Vector2(150, 44),
                                            Action = () => choose(true),
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        private void choose(bool enable)
        {
            Hide();
            Chosen?.Invoke(enable);
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        /// <summary>Swallows clicks inside the panel so they don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
