using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// A vertical-node timeline of a collab's revision history: one node per version, newest at the top, each
    /// showing who pushed it, when, an optional message, and simple object stats (circles / sliders / spinners).
    /// </summary>
    public partial class CollabRevisionsOverlay : VisibilityContainer
    {
        private Container panel = null!;
        private SpriteText subtitle = null!;
        private FillFlowContainer list = null!;

        private Func<Task<List<CollabRevisionSummary>>>? fetch;

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
                    Size = new Vector2(540, 560),
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
                            Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "Revision history",
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Title(),
                                },
                                subtitle = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                    Truncate = true,
                                    RelativeSizeAxes = Axes.X,
                                    Margin = new MarginPadding { Bottom = EditorTheme.Spacing.Sm },
                                },
                                new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 410,
                                    ScrollbarVisible = false,
                                    Child = list = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                    },
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

        /// <summary>Opens the timeline for a collab, lazily fetching its revisions.</summary>
        public void Show(string title, Func<Task<List<CollabRevisionSummary>>> fetchRevisions)
        {
            subtitle.Text = string.IsNullOrEmpty(title) ? string.Empty : title;
            fetch = fetchRevisions;
            Show();
        }

        private void reload()
        {
            list.Clear();
            list.Add(message("Loading..."));

            if (fetch == null)
                return;

            fetch().ContinueWith(t =>
            {
                var revs = t.IsCompletedSuccessfully ? t.Result : new List<CollabRevisionSummary>();
                Schedule(() => populate(revs));
            });
        }

        private void populate(List<CollabRevisionSummary> revs)
        {
            list.Clear();

            if (revs.Count == 0)
            {
                list.Add(message("No revisions pushed yet."));
                return;
            }

            for (int i = 0; i < revs.Count; i++)
                list.Add(node(revs[i], isFirst: i == 0, isLast: i == revs.Count - 1));
        }

        // One timeline node: a dot in a left gutter (joined by a line to its neighbours) + the revision details.
        private Drawable node(CollabRevisionSummary r, bool isFirst, bool isLast)
        {
            const float gutter = 36f;
            const float dot = 12f;

            var children = new List<Drawable>
            {
                // The connecting line, split above/below the dot so the ends are clean at the first/last node.
                new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopCentre,
                    X = gutter / 2f,
                    Width = 2,
                    RelativeSizeAxes = Axes.Y,
                    Height = 0.5f,
                    Colour = EditorTheme.Colours.Selection,
                    Alpha = isFirst ? 0 : 1,
                },
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomCentre,
                    X = gutter / 2f,
                    Width = 2,
                    RelativeSizeAxes = Axes.Y,
                    Height = 0.5f,
                    Colour = EditorTheme.Colours.Selection,
                    Alpha = isLast ? 0 : 1,
                },
                new Circle
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = new Vector2(gutter / 2f, 26),
                    Size = new Vector2(dot),
                    Colour = isFirst ? EditorTheme.Colours.Accent : EditorTheme.Colours.Text,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Padding = new MarginPadding { Left = gutter, Right = EditorTheme.Spacing.Sm, Vertical = EditorTheme.Spacing.Sm },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = $"Revision {r.Number}" + (isFirst ? "  (latest)" : string.Empty) + $"  -  by {r.AuthorUsername ?? "?"}",
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.BodyStrong(),
                        },
                        new SpriteText
                        {
                            Text = $"{formatDate(r.CreatedAt)}   -   {statsLine(r)}",
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Caption(),
                        },
                        messageLine(r.Message),
                    },
                },
            };

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = children.ToArray(),
            };
        }

        private static string statsLine(CollabRevisionSummary r) =>
            $"{r.Circles} circles  -  {r.Sliders} sliders  -  {r.Spinners} spinners";

        private static string formatDate(DateTimeOffset when) =>
            when.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

        private Drawable messageLine(string? msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return new Container { Height = 0 };

            return new SpriteText
            {
                Text = $"\"{msg.Trim()}\"",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(),
                Truncate = true,
                RelativeSizeAxes = Axes.X,
                Margin = new MarginPadding { Top = 2 },
            };
        }

        private SpriteText message(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Body(),
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
        };

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            reload();
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
