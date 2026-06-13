using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A lightweight transient-notification layer: brief "toasts" that pop in at the top of the screen
    /// to confirm an action (e.g. reloading the library with F5), then auto-dismiss. Stacks vertically so
    /// rapid actions don't clobber each other. Place it in front of the screen's content.
    /// </summary>
    public partial class ToastOverlay : CompositeDrawable
    {
        private FillFlowContainer flow = null!;

        public ToastOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = flow = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                Margin = new MarginPadding { Top = 88 },
            };
        }

        /// <summary>Shows a toast. <paramref name="accent"/> defaults to the neutral info colour.</summary>
        public void Push(string message, Color4? accent = null)
            => flow.Add(new Toast(message, accent ?? EditorTheme.Colours.Info));

        private partial class Toast : CompositeDrawable
        {
            private readonly string message;
            private readonly Color4 accent;

            public Toast(string message, Color4 accent)
            {
                this.message = message;
                this.accent = accent;

                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = Color4.Black.Opacity(0.4f),
                        Radius = 12,
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Overlay,
                            Alpha = 0.97f,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Md },
                            Children = new Drawable[]
                            {
                                new Circle
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(8),
                                    Colour = accent,
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = message,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.BodyStrong(),
                                },
                            },
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Pop in, hold, then fade out and remove itself from the flow.
                this.FadeInFromZero(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

                using (BeginDelayedSequence(2000))
                    this.FadeOut(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease).Expire();
            }
        }
    }
}
