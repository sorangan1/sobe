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
    /// Lists the collabs the logged-in user belongs to (from the server) and, per row, lets them download
    /// ("clone") one they don't have yet or pull the latest changes onto one they do. Opened from song select.
    /// </summary>
    public partial class CollabsListOverlay : VisibilityContainer
    {
        public Func<bool>? IsLoggedIn;
        public Func<Task<List<CollabSummary>>>? Fetch;
        public Func<CollabSummary, Task<(bool ok, string message)>>? Download;
        public Func<CollabSummary, Task<(bool ok, string message)>>? Pull;
        public CollabSession? Session;

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
                    Size = new Vector2(540, 480),
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
                                    Height = 340,
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
            Fetch().ContinueWith(t =>
            {
                var collabs = t.IsCompletedSuccessfully ? t.Result : new List<CollabSummary>();
                Schedule(() => populate(collabs));
            });
        }

        private void populate(List<CollabSummary> collabs)
        {
            list.Clear();

            if (collabs.Count == 0)
            {
                list.Add(message("No collabs yet. Start one from a difficulty's COLLAB chip."));
                return;
            }

            foreach (var c in collabs)
                list.Add(row(c));
        }

        private Drawable row(CollabSummary c)
        {
            bool downloaded = Session?.IsLinkedTo(c.Id) == true;
            int localBase = localBaseOf(c.Id);
            bool changes = downloaded && c.HeadRevision > localBase;

            string actionLabel = !downloaded ? "Download" : changes ? $"Pull (rev {c.HeadRevision})" : "Up to date";
            bool actionable = !downloaded || changes;

            var button = new OsuButton(actionLabel, actionable ? OsuColour.Pink : OsuColour.Surface)
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Size = new Vector2(160, 40),
                Action = () =>
                {
                    if (!downloaded)
                        run(Download, c);
                    else if (changes)
                        run(Pull, c);
                },
            };
            button.Enabled.Value = actionable;

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 56,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface, Alpha = 0.5f },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.66f,
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
                                Text = $"by {c.OwnerUsername ?? "?"} - revision {c.HeadRevision}" + (changes ? " - changes available" : string.Empty),
                                Colour = changes ? EditorTheme.Colours.Warning : EditorTheme.Colours.TextMuted,
                                Font = EditorTheme.Type.Caption(),
                            },
                        },
                    },
                    button,
                },
            };
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
    }
}
