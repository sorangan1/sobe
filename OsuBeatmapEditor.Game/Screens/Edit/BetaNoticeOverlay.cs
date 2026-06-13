using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A one-time welcome/beta notice shown when the editor opens: it states the build version and that the
    /// editor is a work in progress that may contain bugs. A toggle lets the user decide whether the notice
    /// appears again on future opens (persisted via <see cref="EditorSettings.ShowBetaPopup"/>).
    /// </summary>
    public partial class BetaNoticeOverlay : VisibilityContainer
    {
        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        private Container panel = null!;

        protected override bool StartHidden => true;

        public BetaNoticeOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 460,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = AppInfo.Name,
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Display(),
                                },
                                new SpriteText
                                {
                                    Text = $"version {AppInfo.Version}",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Label(),
                                },
                                body(
                                    "Welcome to sobe - the sorangan osu! beatmap editor.\n\n"
                                    + "This is an early beta. Lots of features work, but you will run into rough "
                                    + "edges and bugs. Keep backups of maps you care about, and expect things to "
                                    + "change between versions.\n\n"
                                    + "Thanks for trying it out!"),
                                new Container { RelativeSizeAxes = Axes.X, Height = EditorTheme.Spacing.Xs },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 26,
                                    Children = new Drawable[]
                                    {
                                        new SpriteText
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Text = "Show this notice next time",
                                            Colour = EditorTheme.Colours.Text,
                                            Font = EditorTheme.Type.Body(),
                                        },
                                        new ToggleSwitch(settings.ShowBetaPopup)
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                        },
                                    },
                                },
                                new OsuButton("Got it", EditorTheme.Colours.Accent)
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Size = new Vector2(120, EditorTheme.Sizing.ButtonHeight),
                                    Action = Hide,
                                },
                            },
                        },
                    },
                },
            };
        }

        private static Drawable body(string text)
        {
            var flow = new TextFlowContainer(t =>
            {
                t.Colour = EditorTheme.Colours.Text;
                t.Font = EditorTheme.Type.Body();
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };
            flow.AddText(text);
            return flow;
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, 400, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.97f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.KeypadEnter)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>Swallows clicks so interacting with the panel doesn't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
