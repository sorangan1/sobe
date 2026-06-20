using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The collab ("git for maps") panel, opened by the top-bar <see cref="CollabButton"/>. Shows whether the
    /// open difficulty is a collab and lets the user start one or add a collaborator by osu! username. All the
    /// orchestration (create + initial push + asset upload, add member) lives in the editor; this view just calls
    /// the supplied delegates and reflects their result.
    /// </summary>
    public partial class CollabOverlay : VisibilityContainer
    {
        /// <summary>True when a sobe session is active (collab requires login).</summary>
        public Func<bool>? IsLoggedIn;

        /// <summary>The open diff's collab link, or null if it isn't a collab yet.</summary>
        public Func<CollabLink?>? CurrentLink;

        /// <summary>Starts a collab for the open diff (create + initial push + asset upload). Returns (ok, message).</summary>
        public Func<Task<(bool ok, string message)>>? StartCollab;

        /// <summary>Adds a collaborator by osu! username. Returns (ok, message).</summary>
        public Func<string, Task<(bool ok, string message)>>? AddMember;

        /// <summary>Uploads the current map state as a new collab revision (manual push). Returns (ok, message).</summary>
        public Func<Task<(bool ok, string message)>>? PushProgress;

        private Container panel = null!;
        private FillFlowContainer content = null!;
        private SpriteText statusText = null!;
        private bool busy;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(460, 300),
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
                            Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Collab",
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Title(),
                                },
                                content = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                },
                                statusText = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                    Alpha = 0,
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                },
                            },
                        },
                        new OsuButton("Close", OsuColour.Surface)
                        {
                            Anchor = Anchor.BottomRight,
                            Origin = Anchor.BottomRight,
                            Margin = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Size = new Vector2(120, 44),
                            Action = () => Hide(),
                        },
                    },
                },
            };
        }

        private void rebuild()
        {
            content.Clear();

            if (IsLoggedIn?.Invoke() != true)
            {
                content.Add(label("Log into sobe to use collab.", EditorTheme.Colours.TextMuted));
                return;
            }

            var link = CurrentLink?.Invoke();
            if (link == null)
            {
                content.Add(label("This difficulty isn't a collab yet.", EditorTheme.Colours.TextMuted));
                content.Add(label("Start one to share it with a collaborator.", EditorTheme.Colours.TextMuted));
                content.Add(new OsuButton("Start collab", OsuColour.Pink)
                {
                    Size = new Vector2(180, 44),
                    Action = runStart,
                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                });
                return;
            }

            content.Add(label($"Collab active - revision {link.BaseRevision}.", EditorTheme.Colours.Text));
            content.Add(label("Saving keeps your work local. Upload progress to share it as a new revision.", EditorTheme.Colours.TextMuted));
            content.Add(new OsuButton("Upload progress", OsuColour.Pink)
            {
                Size = new Vector2(200, 44),
                Action = runPush,
                Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs, Bottom = EditorTheme.Spacing.Sm },
            });
            content.Add(label("Invite collaborator (osu! username):", EditorTheme.Colours.TextMuted));

            var memberBox = new BasicTextBox
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.InputHeight,
                PlaceholderText = "username",
            };
            memberBox.OnCommit += (_, _) => runAddMember(memberBox);

            content.Add(memberBox);
            content.Add(new OsuButton("Invite", OsuColour.Pink)
            {
                Size = new Vector2(140, 44),
                Action = () => runAddMember(memberBox),
            });
        }

        private SpriteText label(string text, Color4 colour) => new SpriteText
        {
            Text = text,
            Colour = colour,
            Font = EditorTheme.Type.Body(),
        };

        private void runStart()
        {
            if (busy || StartCollab == null)
                return;

            setBusy(true);
            StartCollab().ContinueWith(t =>
            {
                var (ok, msg) = t.IsCompletedSuccessfully ? t.Result : (false, "Something went wrong.");
                Schedule(() =>
                {
                    setBusy(false);
                    showStatus(msg, ok);
                    rebuild();
                });
            });
        }

        private void runAddMember(BasicTextBox memberBox)
        {
            string username = memberBox.Text.Trim();
            if (busy || username.Length == 0 || AddMember == null)
                return;

            setBusy(true);
            AddMember(username).ContinueWith(t =>
            {
                var (ok, msg) = t.IsCompletedSuccessfully ? t.Result : (false, "Something went wrong.");
                Schedule(() =>
                {
                    setBusy(false);
                    showStatus(msg, ok);
                    if (ok)
                        memberBox.Text = string.Empty;
                });
            });
        }

        private void runPush()
        {
            if (busy || PushProgress == null)
                return;

            setBusy(true);
            PushProgress().ContinueWith(t =>
            {
                var (ok, msg) = t.IsCompletedSuccessfully ? t.Result : (false, "Something went wrong.");
                Schedule(() =>
                {
                    setBusy(false);
                    showStatus(msg, ok);
                    // Reflect the advanced revision number in the panel header.
                    if (ok)
                        rebuild();
                });
            });
        }

        private void setBusy(bool value)
        {
            busy = value;
            if (value)
            {
                statusText.Text = "Working...";
                statusText.Colour = EditorTheme.Colours.TextMuted;
                statusText.Alpha = 1;
            }
        }

        private void showStatus(string message, bool ok)
        {
            statusText.Text = message;
            statusText.Colour = ok ? EditorTheme.Colours.Success : EditorTheme.Colours.Error;
            statusText.Alpha = string.IsNullOrEmpty(message) ? 0 : 1;
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            statusText.Alpha = 0;
            busy = false;
            rebuild();
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>Swallows clicks inside the panel so they don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
