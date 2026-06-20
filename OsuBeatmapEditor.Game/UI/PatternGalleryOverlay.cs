using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The Pattern Gallery: a panel that slides up from the bottom showing the logged-in user's saved
    /// patterns as visual cards. Patterns can be renamed, duplicated, deleted, filtered by name, organised
    /// into collections (folders), and dropped into the open map at the current playhead. Backed by our
    /// server (<see cref="SobeApi"/>), so a user's library follows their profile across machines.
    /// </summary>
    public partial class PatternGalleryOverlay : VisibilityContainer
    {
        /// <summary>
        /// Pastes a deserialized pattern (objects + per-slider source velocities + source beat length) into the
        /// open map at the current snapped playhead, preserving rhythmic length + spacing.
        /// </summary>
        public Action<PatternSerializer.DeserializedPattern>? AddToMap;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private PatternStore? store { get; set; }

        private Container panel = null!;
        private Container body = null!;
        private FillFlowContainer rail = null!;
        private FillFlowContainer cards = null!;
        private GalleryTextBox searchBox = null!;
        private Container railNewBox = null!;

        private readonly List<PatternSummary> patterns = new List<PatternSummary>();
        private readonly List<PatternCollectionInfo> collections = new List<PatternCollectionInfo>();
        private readonly Dictionary<Guid, string> contentCache = new Dictionary<Guid, string>();

        private Guid? activeCollection;   // null = "All"
        private string search = string.Empty;
        private bool loading;

        protected override bool StartHidden => true;

        public PatternGalleryOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.5f },
                panel = new ClickBlockingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1f, 0.52f),
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = new Color4(0, 0, 0, 0.4f),
                        Radius = 14f,
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                        buildHeader(),
                        body = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = header_height },
                        },
                    },
                },
            };
        }

        private const float header_height = 52f;
        private const float rail_width = 184f;

        private Drawable buildHeader()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = header_height,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = EditorTheme.Sizing.BorderThickness,
                        Colour = EditorTheme.Colours.Border,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl },
                        Text = "Pattern Gallery",
                        Colour = EditorTheme.Colours.Accent,
                        Font = EditorTheme.Type.Title(),
                    },
                    searchBox = new GalleryTextBox
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = 56 },
                        Width = 240,
                        Height = EditorTheme.Sizing.InputHeight,
                        PlaceholderText = "Search patterns...",
                    },
                    new CloseButton(() => Hide())
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = EditorTheme.Spacing.Lg },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            searchBox.Current.ValueChanged += e =>
            {
                search = e.NewValue ?? string.Empty;
                rebuildCards();
            };
        }

        // --- Visibility: slide up from the bottom edge. ---

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.MoveToY(0, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            reload();
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.MoveToY(panel.DrawHeight, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
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

        /// <summary>Forces a backend refresh of the gallery (e.g. after a duplicate / collection change).</summary>
        public void RefreshPatterns() => reload(force: true);

        /// <summary>Re-renders the gallery from the local cache only (no network) - for an optimistic local save.</summary>
        public void RefreshFromCache()
        {
            if (auth?.Token == null)
                return;
            loadFromCache();
            rebuildAll();
        }

        /// <summary>
        /// Renders the gallery from the local cache immediately, then refreshes from the backend only when the
        /// cache is stale (a TTL) or <paramref name="force"/>d - so reopening the gallery doesn't re-hit the DB.
        /// </summary>
        private void reload(bool force = false)
        {
            var user = auth?.User.Value;
            string? token = auth?.Token;
            if (token == null || user == null)
            {
                showLoggedOut();
                return;
            }

            store?.EnsureUser(user.Id);
            loadFromCache();

            if (patterns.Count > 0)
                rebuildAll();
            else
                showMessage("Loading your patterns...");

            // Hit the backend only when there's nothing cached, the cache is stale, or a refresh was forced.
            if (force || store == null || store.IsStale || patterns.Count == 0)
                backgroundSync(token, user.Id);
        }

        /// <summary>Populates the in-memory lists (and preview content) from the on-disk cache.</summary>
        private void loadFromCache()
        {
            patterns.Clear();
            collections.Clear();
            contentCache.Clear();

            if (store == null)
                return;

            collections.AddRange(store.Collections);
            foreach (var p in store.Patterns)
            {
                patterns.Add(new PatternSummary
                {
                    Id = p.Id,
                    Name = p.Name,
                    CollectionId = p.CollectionId,
                    ObjectCount = p.ObjectCount,
                    UpdatedAt = p.UpdatedAt,
                });
                if (p.Content != null)
                    contentCache[p.Id] = p.Content;
            }

            if (activeCollection is { } id && collections.All(c => c.Id != id))
                activeCollection = null;
        }

        /// <summary>Fetches the pattern list + collections from the backend (one query each) and updates the cache.</summary>
        private void backgroundSync(string token, long userId)
        {
            Task.Run(async () =>
            {
                var pats = await SobeApi.GetMyPatternsAsync(token).ConfigureAwait(false);
                var cols = await SobeApi.GetMyCollectionsAsync(token).ConfigureAwait(false);
                Schedule(() =>
                {
                    loading = false;
                    if (store != null)
                    {
                        store.SyncList(userId, pats, cols);
                        loadFromCache();
                    }
                    else
                    {
                        patterns.Clear();
                        patterns.AddRange(pats);
                        collections.Clear();
                        collections.AddRange(cols);
                        if (activeCollection is { } id && collections.All(c => c.Id != id))
                            activeCollection = null;
                    }
                    rebuildAll();
                });
            });
        }

        private void rebuildAll()
        {
            body.Clear();
            body.Children = new Drawable[]
            {
                rail = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = rail_width,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(EditorTheme.Spacing.Md),
                    Spacing = new Vector2(0, 2),
                },
                new Box
                {
                    X = rail_width,
                    RelativeSizeAxes = Axes.Y,
                    Width = EditorTheme.Sizing.BorderThickness,
                    Colour = EditorTheme.Colours.Border,
                },
                new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = rail_width + EditorTheme.Spacing.Md },
                    ScrollbarVisible = false,
                    Child = cards = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Padding = new MarginPadding(EditorTheme.Spacing.Md),
                        Spacing = new Vector2(EditorTheme.Spacing.Md),
                    },
                },
            };

            rebuildRail();
            rebuildCards();
        }

        private void rebuildRail()
        {
            rail.Clear();
            rail.Add(new RailItem("All patterns", activeCollection == null, () => setCollection(null)));
            foreach (var c in collections)
            {
                var id = c.Id;
                rail.Add(new RailItem(c.Name, activeCollection == id, () => setCollection(id),
                    onDelete: () => deleteCollection(id)));
            }
            railNewBox = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y };
            rail.Add(new RailItem("+ New collection", false, beginNewCollection));
            rail.Add(railNewBox);
        }

        private void setCollection(Guid? id)
        {
            activeCollection = id;
            rebuildRail();
            rebuildCards();
        }

        private void rebuildCards()
        {
            if (cards == null)
                return;

            cards.Clear();

            var filtered = patterns
                .Where(p => activeCollection == null || p.CollectionId == activeCollection)
                .Where(p => string.IsNullOrEmpty(search) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count == 0)
            {
                cards.Add(new SpriteText
                {
                    Text = loading ? string.Empty
                        : patterns.Count == 0
                            ? "No saved patterns yet - select objects, hold Shift and click \"Save pattern\"."
                            : "No patterns match.",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                });
                return;
            }

            foreach (var p in filtered)
            {
                var card = new PatternCard(p, collections)
                {
                    OnAdd = () => addToMap(p),
                    OnDuplicate = () => duplicate(p),
                    OnDelete = () => deletePattern(p),
                    OnRename = name => rename(p, name),
                    OnMove = colId => move(p, colId),
                };
                cards.Add(card);
                loadPreview(p, card);
            }
        }

        private void loadPreview(PatternSummary p, PatternCard card)
        {
            if (contentCache.TryGetValue(p.Id, out var cached))
            {
                card.SetPreview(PatternSerializer.Deserialize(cached));
                return;
            }

            string? token = auth?.Token;
            if (token == null)
                return;

            Task.Run(async () =>
            {
                var content = await SobeApi.GetPatternAsync(token, p.Id).ConfigureAwait(false);
                if (content == null)
                    return;
                Schedule(() =>
                {
                    contentCache[p.Id] = content.Content;
                    store?.SetContent(p.Id, content.Content);
                    card.SetPreview(PatternSerializer.Deserialize(content.Content));
                });
            });
        }

        // --- Card actions ---

        private void addToMap(PatternSummary p)
        {
            void paste(string content) => AddToMap?.Invoke(PatternSerializer.DeserializeFull(content));

            if (contentCache.TryGetValue(p.Id, out var cached))
            {
                paste(cached);
                Hide();
                return;
            }

            string? token = auth?.Token;
            if (token == null)
                return;

            Task.Run(async () =>
            {
                var content = await SobeApi.GetPatternAsync(token, p.Id).ConfigureAwait(false);
                if (content == null)
                    return;
                Schedule(() =>
                {
                    contentCache[p.Id] = content.Content;
                    store?.SetContent(p.Id, content.Content);
                    paste(content.Content);
                    Hide();
                });
            });
        }

        private void duplicate(PatternSummary p)
        {
            string? token = auth?.Token;
            if (token == null)
                return;

            Task.Run(async () =>
            {
                string content = contentCache.TryGetValue(p.Id, out var c)
                    ? c
                    : (await SobeApi.GetPatternAsync(token, p.Id).ConfigureAwait(false))?.Content ?? string.Empty;
                if (string.IsNullOrEmpty(content))
                    return;

                await SobeApi.CreatePatternAsync(token, p.Name + " copy", p.CollectionId, content, p.ObjectCount).ConfigureAwait(false);
                Schedule(() => reload(force: true));
            });
        }

        private void deletePattern(PatternSummary p)
        {
            string? token = auth?.Token;
            if (token == null)
                return;
            Task.Run(async () =>
            {
                await SobeApi.DeletePatternAsync(token, p.Id).ConfigureAwait(false);
            });
            // Optimistic: drop it locally + from the cache straight away (no refetch).
            contentCache.Remove(p.Id);
            patterns.RemoveAll(x => x.Id == p.Id);
            store?.Remove(p.Id);
            rebuildCards();
        }

        private void rename(PatternSummary p, string name)
        {
            string? token = auth?.Token;
            if (token == null || string.IsNullOrWhiteSpace(name))
                return;
            p.Name = name.Trim();
            store?.Rename(p.Id, p.Name);
            Task.Run(() => SobeApi.RenamePatternAsync(token, p.Id, p.Name));
        }

        private void move(PatternSummary p, Guid? colId)
        {
            string? token = auth?.Token;
            if (token == null)
                return;
            p.CollectionId = colId;
            store?.Move(p.Id, colId);
            Task.Run(() => SobeApi.MovePatternAsync(token, p.Id, colId));
            rebuildCards();
        }

        // --- Collection management ---

        private void beginNewCollection()
        {
            railNewBox.Child = new GalleryTextBox
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.InputHeight,
                PlaceholderText = "Name...",
                CommitOnFocusLost = false,
                OnCommitText = name =>
                {
                    railNewBox.Clear();
                    if (!string.IsNullOrWhiteSpace(name))
                        createCollection(name.Trim());
                },
                AutoFocus = true,
            };
        }

        private void createCollection(string name)
        {
            string? token = auth?.Token;
            if (token == null)
                return;
            Task.Run(async () =>
            {
                await SobeApi.CreateCollectionAsync(token, name).ConfigureAwait(false);
                Schedule(() => reload(force: true));
            });
        }

        private void deleteCollection(Guid id)
        {
            string? token = auth?.Token;
            if (token == null)
                return;
            Task.Run(async () =>
            {
                await SobeApi.DeleteCollectionAsync(token, id).ConfigureAwait(false);
                Schedule(() => reload(force: true));
            });
        }

        // --- Empty / message states ---

        private void showLoggedOut() => showMessage("Log in to save and sync your patterns.", showLogin: true);

        private void showMessage(string text, bool showLogin = false)
        {
            body.Clear();
            var flow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Body(),
                    },
                },
            };

            if (showLogin)
            {
                flow.Add(new OsuButton("Log in", EditorTheme.Colours.Accent)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(140, EditorTheme.Sizing.ButtonHeight),
                    Action = () => auth?.Login(),
                });
            }

            body.Add(flow);
        }

        /// <summary>Swallows clicks so interacting with the panel doesn't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
