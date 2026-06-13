using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Updates;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Main-menu notice shown when a newer version is available: a message plus a button to download/install
    /// it. Reflects the <see cref="UpdateManager"/> state (available → downloading → ready to restart).
    /// </summary>
    public partial class UpdateBanner : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private UpdateManager? updates { get; set; }

        private SpriteText label = null!;
        private OsuButton actionButton = null!;

        public UpdateBanner()
        {
            AutoSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                        Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Sm },
                        Children = new Drawable[]
                        {
                            label = new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Body(),
                            },
                            actionButton = new OsuButton("Update", OsuColour.Pink)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(150, 36),
                                Action = onAction,
                            },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (updates == null)
                return;

            updates.State.BindValueChanged(_ => refresh(), true);
            updates.Progress.BindValueChanged(_ => refresh());
        }

        private void onAction()
        {
            if (updates == null)
                return;

            switch (updates.State.Value)
            {
                case UpdateState.UpdateAvailable:
                    if (updates.CanSelfInstall)
                        _ = updates.PrepareAsync();
                    else
                        updates.OpenReleasesPage();
                    break;

                case UpdateState.ReadyToRestart:
                    updates.RestartAndApply();
                    break;
            }
        }

        private void refresh()
        {
            if (updates == null)
                return;

            string version = updates.LatestVersion.Value;

            switch (updates.State.Value)
            {
                case UpdateState.UpdateAvailable:
                    label.Text = $"Version {version} is available";
                    actionButton.Text = updates.CanSelfInstall ? "Update" : "Download";
                    actionButton.Enabled.Value = true;
                    actionButton.Alpha = 1;
                    this.FadeIn(200, Easing.OutQuint);
                    break;

                case UpdateState.Downloading:
                    label.Text = $"Downloading {version}... {updates.Progress.Value * 100:0}%";
                    actionButton.Alpha = 0;
                    actionButton.Enabled.Value = false;
                    this.FadeIn(200, Easing.OutQuint);
                    break;

                case UpdateState.ReadyToRestart:
                    label.Text = $"Version {version} ready";
                    actionButton.Text = "Restart now";
                    actionButton.Enabled.Value = true;
                    actionButton.Alpha = 1;
                    this.FadeIn(200, Easing.OutQuint);
                    break;

                default:
                    this.FadeOut(200, Easing.OutQuint);
                    break;
            }
        }
    }
}
