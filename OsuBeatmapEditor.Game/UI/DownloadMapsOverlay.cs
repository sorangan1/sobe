using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using OsuBeatmapEditor.Game.Screens.SongSelect;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Search osu!'s beatmap listing (proxied through the sobe backend) and, per result, open the set's
    /// <c>.osz</c> download link in the browser. The downloaded archive can then be dragged onto the window to
    /// import it (see <see cref="OsuBeatmapEditor.Game.Beatmaps.BeatmapArchiveImporter"/>). Opened from song select.
    /// </summary>
    public partial class DownloadMapsOverlay : VisibilityContainer
    {
        /// <summary>Opens a URL in the user's browser (the host's OpenUrlExternally).</summary>
        public Action<string>? OpenUrl;

        private CarouselSearchTextBox searchBox = null!;
        private FillFlowContainer list = null!;
        private BasicScrollContainer scroll = null!;
        private ScheduledDelegate? searchDebounce;
        private int requestId;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(620, 540),
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
                                    Text = "Download maps",
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = EditorTheme.Type.Title(),
                                },
                                new SpriteText
                                {
                                    Text = "Search osu!, then download the .osz and drag it onto the window to import.",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                },
                                searchBox = new CarouselSearchTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 40,
                                    PlaceholderText = "Search by title, artist or mapper...",
                                },
                                scroll = new BasicScrollContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 360,
                                    ScrollbarVisible = false,
                                    Child = list = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
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

            // Debounce so a fast typer doesn't fire a request per keystroke.
            searchBox.Current.BindValueChanged(e =>
            {
                searchDebounce?.Cancel();
                searchDebounce = Scheduler.AddDelayed(() => runSearch(e.NewValue), 350);
            });
        }

        private void runSearch(string query)
        {
            int id = ++requestId;
            scroll.ScrollToStart(false);

            if (string.IsNullOrWhiteSpace(query))
            {
                list.Clear();
                list.Add(message("Type to search osu!'s beatmap listing."));
                return;
            }

            list.Clear();
            list.Add(message("Searching..."));

            SobeApi.SearchBeatmapsetsAsync(query).ContinueWith(t =>
            {
                var results = t.IsCompletedSuccessfully ? t.Result : new List<BeatmapSetSearchResult>();
                Schedule(() =>
                {
                    if (id != requestId)
                        return; // a newer search superseded this one

                    populate(results);
                });
            });
        }

        private void populate(List<BeatmapSetSearchResult> results)
        {
            list.Clear();

            if (results.Count == 0)
            {
                list.Add(message("No results. Try a different search."));
                return;
            }

            foreach (var r in results)
                list.Add(row(r));
        }

        private Drawable row(BeatmapSetSearchResult r)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 56,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Sm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface, Alpha = 0.5f },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.74f,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Left = EditorTheme.Spacing.Lg },
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = $"{r.Artist} - {r.Title}",
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Body(),
                                Truncate = true,
                                RelativeSizeAxes = Axes.X,
                            },
                            new SpriteText
                            {
                                Text = $"mapped by {r.Creator}" + (string.IsNullOrEmpty(r.Status) ? string.Empty : $" - {r.Status}"),
                                Colour = EditorTheme.Colours.TextMuted,
                                Font = EditorTheme.Type.Caption(),
                            },
                        },
                    },
                    new OsuButton("Download", OsuColour.Pink)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = EditorTheme.Spacing.Lg },
                        Size = new Vector2(120, 40),
                        Action = () => OpenUrl?.Invoke(r.DownloadUrl),
                    },
                },
            };
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
            runSearch(searchBox.Current.Value);
            Schedule(() => GetContainingFocusManager()?.ChangeFocus(searchBox));
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
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
