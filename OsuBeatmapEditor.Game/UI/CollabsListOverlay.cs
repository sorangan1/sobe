using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Lists the collabs the logged-in user belongs to, plus any pending invites to accept/decline. Per collab
    /// row the user can download ("clone") one they don't have yet, pull the latest changes, open the local map,
    /// or view its revision history. Opened from song select.
    /// </summary>
    public partial class CollabsListOverlay : VisibilityContainer
    {
        public Func<bool>? IsLoggedIn;
        public Func<Task<List<CollabSummary>>>? Fetch;
        public Func<Task<List<CollabInvite>>>? FetchInvites;
        public Func<CollabInvite, Task<bool>>? Accept;
        public Func<CollabInvite, Task<bool>>? Decline;
        public Func<CollabSummary, Task<(bool ok, string message)>>? Download;
        public Func<CollabSummary, Task<(bool ok, string message)>>? Pull;
        public CollabSession? Session;

        /// <summary>True when the collab's linked local map still exists (false if never downloaded or deleted).</summary>
        public Func<CollabSummary, bool>? IsDownloaded;

        /// <summary>Opens the collab's local map in the editor (only offered when it's downloaded).</summary>
        public Action<CollabSummary>? OpenMap;

        /// <summary>Opens the revision-history timeline for a collab.</summary>
        public Action<CollabSummary>? ShowInfo;

        /// <summary>Invoked after a successful download/pull so the host can reload its beatmap library.</summary>
        public Action? OnLibraryChanged;

        private Container panel = null!;
        private FillFlowContainer list = null!;
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
                    Size = new Vector2(560, 520),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Collabs",
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Title(),
                                },
                                new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 380,
                                    ScrollbarVisible = false,
                                    Child = list = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                                    },
                                },
                                statusText = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                    Alpha = 0,
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

        /// <summary>Re-fetches invites + collabs from the server and rebuilds the list. Safe to call repeatedly.</summary>
        public void Refresh() => refresh();

        private void refresh()
        {
            list.Clear();
            statusText.Alpha = 0;

            if (IsLoggedIn?.Invoke() != true)
            {
                list.Add(message("Log into sobe to see your collabs."));
                return;
            }

            if (Fetch == null)
                return;

            list.Add(message("Loading..."));

            var invitesTask = FetchInvites?.Invoke() ?? Task.FromResult(new List<CollabInvite>());
            var collabsTask = Fetch();
            Task.WhenAll(invitesTask, collabsTask).ContinueWith(_ =>
            {
                var invites = invitesTask.IsCompletedSuccessfully ? invitesTask.Result : new List<CollabInvite>();
                var collabs = collabsTask.IsCompletedSuccessfully ? collabsTask.Result : new List<CollabSummary>();
                Schedule(() => populate(invites, collabs));
            });
        }

        private void populate(List<CollabInvite> invites, List<CollabSummary> collabs)
        {
            list.Clear();

            if (invites.Count > 0)
            {
                list.Add(sectionHeader("INVITES"));
                foreach (var i in invites)
                    list.Add(inviteRow(i));
            }

            if (collabs.Count == 0 && invites.Count == 0)
            {
                list.Add(message("No collabs yet. Start one from a difficulty's COLLAB chip."));
                return;
            }

            if (collabs.Count > 0)
            {
                if (invites.Count > 0)
                    list.Add(sectionHeader("YOUR COLLABS"));
                foreach (var c in collabs)
                    list.Add(row(c));
            }
        }

        private Drawable inviteRow(CollabInvite invite)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 56,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent, Alpha = 0.12f },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.55f,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Left = EditorTheme.Spacing.Lg },
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = string.IsNullOrEmpty(invite.Title) ? "(untitled)" : invite.Title,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Body(),
                                Truncate = true,
                                RelativeSizeAxes = Axes.X,
                            },
                            new SpriteText
                            {
                                Text = $"invited by {invite.OwnerUsername ?? "?"}",
                                Colour = EditorTheme.Colours.Accent,
                                Font = EditorTheme.Type.Caption(),
                            },
                        },
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                        Margin = new MarginPadding { Right = EditorTheme.Spacing.Lg },
                        Children = new Drawable[]
                        {
                            new OsuButton("Decline", OsuColour.Surface)
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Size = new Vector2(100, 40),
                                Action = () => runInvite(Decline, invite, "Invite declined."),
                            },
                            new OsuButton("Accept", OsuColour.Pink)
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Size = new Vector2(100, 40),
                                Action = () => runInvite(Accept, invite, "Joined the collab. Download it below."),
                            },
                        },
                    },
                },
            };
        }

        private Drawable row(CollabSummary c)
        {
            bool downloaded = IsDownloaded?.Invoke(c) ?? false;
            int localBase = localBaseOf(c.Id);
            bool changes = downloaded && c.HeadRevision > localBase;

            string actionLabel = !downloaded ? "Download" : changes ? $"Pull (rev {c.HeadRevision})" : "Up to date";
            bool actionable = !downloaded || changes;

            var actionButton = new OsuButton(actionLabel, actionable ? OsuColour.Pink : OsuColour.Surface)
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Size = new Vector2(150, 40),
                Action = () =>
                {
                    if (!downloaded)
                        run(Download, c);
                    else if (changes)
                        run(Pull, c);
                },
            };
            actionButton.Enabled.Value = actionable;

            var buttons = new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                Margin = new MarginPadding { Right = EditorTheme.Spacing.Lg },
                Children = new Drawable[]
                {
                    new OsuButton("Info", OsuColour.Surface)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Size = new Vector2(64, 40),
                        Action = () => ShowInfo?.Invoke(c),
                    },
                    actionButton,
                },
            };

            // The title area opens the local map when it's downloaded (a quick way to jump into it).
            var titleArea = new ClickableInfo(downloaded ? () => OpenMap?.Invoke(c) : null)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.Both,
                Width = 0.62f,
                Child = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Left = EditorTheme.Spacing.Lg },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = string.IsNullOrEmpty(c.Title) ? "(untitled)" : c.Title,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Body(),
                            Truncate = true,
                            RelativeSizeAxes = Axes.X,
                        },
                        new SpriteText
                        {
                            Text = subtitleFor(c, downloaded, changes),
                            Colour = changes ? EditorTheme.Colours.Warning : EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                },
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 56,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface, Alpha = 0.5f },
                    titleArea,
                    buttons,
                },
            };
        }

        private static string subtitleFor(CollabSummary c, bool downloaded, bool changes)
        {
            string who = $"by {c.OwnerUsername ?? "?"} - revision {c.HeadRevision}";
            if (changes)
                return who + " - changes available";
            if (!downloaded)
                return who + " - not downloaded";
            return who + " - click to open";
        }

        private int localBaseOf(Guid collabId)
        {
            string? key = Session?.KeyForCollab(collabId);
            return key != null ? (Session?.Get(key)?.BaseRevision ?? 0) : 0;
        }

        private void run(Func<CollabSummary, Task<(bool ok, string message)>>? op, CollabSummary c)
        {
            if (busy || op == null)
                return;

            setBusy(true);
            op(c).ContinueWith(t =>
            {
                var (ok, msg) = t.IsCompletedSuccessfully ? t.Result : (false, "Something went wrong.");
                Schedule(() =>
                {
                    busy = false;
                    showStatus(msg, ok);
                    if (ok)
                        OnLibraryChanged?.Invoke();
                    refresh();
                });
            });
        }

        private void runInvite(Func<CollabInvite, Task<bool>>? op, CollabInvite invite, string okMessage)
        {
            if (busy || op == null)
                return;

            setBusy(true);
            op(invite).ContinueWith(t =>
            {
                bool ok = t.IsCompletedSuccessfully && t.Result;
                Schedule(() =>
                {
                    busy = false;
                    showStatus(ok ? okMessage : "Couldn't update the invite.", ok);
                    refresh();
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

        private void showStatus(string msg, bool ok)
        {
            statusText.Text = msg;
            statusText.Colour = ok ? EditorTheme.Colours.Success : EditorTheme.Colours.Error;
            statusText.Alpha = string.IsNullOrEmpty(msg) ? 0 : 1;
        }

        private SpriteText sectionHeader(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Caption(),
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs, Left = EditorTheme.Spacing.Xs },
        };

        private SpriteText message(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Body(),
        };

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            busy = false;
            refresh();
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

        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }

        /// <summary>A click target that blocks the overlay's dismiss-on-click and runs an optional action.</summary>
        private partial class ClickableInfo : Container
        {
            private readonly Action? onClick;

            public ClickableInfo(Action? onClick)
            {
                this.onClick = onClick;
            }

            protected override bool OnClick(ClickEvent e)
            {
                onClick?.Invoke();
                return true;
            }

            protected override bool OnHover(HoverEvent e) => onClick != null;
        }
    }
}
