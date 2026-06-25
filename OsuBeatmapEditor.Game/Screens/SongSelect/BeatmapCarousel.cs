using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A right-aligned, scrollable list of beatmap sets with live search filtering and sorting.
    /// Every set - including single-difficulty ones - shows a header card with its individually-selectable
    /// difficulty card(s) beneath. Selection (yellow border) is separate from opening the editor.
    ///
    /// The carousel is virtualised like osu!lazer's: card layout (positions/heights) is computed up
    /// front, but the actual drawables are only materialised when they scroll into view (and disposed
    /// when they leave). This keeps filtering/sorting and scrolling smooth even for large libraries -
    /// previously every card (with a synchronous texture load) was built on the update thread at once.
    /// </summary>
    public partial class BeatmapCarousel : Container
    {
        /// <summary>Invoked when the selected difficulty changes.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? SelectionChanged;

        /// <summary>Invoked when a difficulty's "Edit" context-menu item is chosen.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? EditRequested;

        /// <summary>Invoked when "Create new Difficulty" is chosen, with the template difficulty.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? CreateDifficultyRequested;

        /// <summary>Invoked when "Create new Set" is chosen, with the source difficulty (audio/timing donor).</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? CreateSetRequested;

        /// <summary>Invoked when "Delete set" is chosen on a set-level card.</summary>
        public Action<BeatmapSetModel>? DeleteSetRequested;

        /// <summary>Invoked when "Delete difficulty" is chosen on a difficulty card.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? DeleteDifficultyRequested;

        /// <summary>Invoked when "Export .osz" is chosen, with the set to pack into an archive.</summary>
        public Action<BeatmapSetModel>? ExportSetRequested;

        /// <summary>Texture store used to load card background images. Set before <see cref="SetBeatmaps"/>.</summary>
        public LargeTextureStore? Textures { get; set; }

        private const float card_spacing = 6;
        private const float padding_top = 8;
        private const float padding_bottom = 8;

        // The panels occupy a fixed-width strip on the right; the scroll itself spans the whole screen,
        // so clicking, dragging and scrolling work anywhere - not just over the cards.
        public const float PANEL_STRIP_WIDTH = 556;
        private const float panel_strip_width = PANEL_STRIP_WIDTH;
        private const float panel_strip_margin_right = 24;

        private readonly CarouselScrollContainer scroll;
        private readonly Container panelContainer;
        private readonly SpriteText emptyMessage;

        private readonly List<BeatmapSetModel> allSets = new List<BeatmapSetModel>();
        private readonly HashSet<string> newIdentities = new HashSet<string>();

        // Every visual card in display order (headers + selectable cards), and the selectable subset.
        private readonly List<Card> cards = new List<Card>();
        private readonly List<Card> entries = new List<Card>();

        private Card? selectedCard;
        private string? selectedKey;

        // A selection to apply as soon as the matching card appears (e.g. after an async import + reload).
        // Survives rebuilds until resolved, so a freshly created set/difficulty ends up selected + centred.
        private string? pendingSelectIdentity;
        private string? pendingSelectDiffName;

        private readonly Random random = new Random();

        private string filter = string.Empty;
        private SortMode sort = SortMode.Artist;

        public bool HasSelection => selectedCard != null;

        public BeatmapCarousel()
        {
            InternalChildren = new Drawable[]
            {
                scroll = new CarouselScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = panelContainer = new Container
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Width = panel_strip_width,
                        Margin = new MarginPadding { Right = panel_strip_margin_right },
                    },
                },
                emptyMessage = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = OsuColour.TextMuted,
                    Font = FontUsage.Default.With(size: 18),
                    Alpha = 0,
                },
            };
        }

        public void SetBeatmaps(IReadOnlyList<BeatmapSetModel> sets)
        {
            allSets.Clear();
            allSets.AddRange(sets);
            rebuild();
        }

        /// <summary>
        /// Requests that the given difficulty (or the first difficulty of the set, when
        /// <paramref name="difficultyName"/> is null) become selected and centred as soon as it appears in
        /// the carousel. Used after creating a set/difficulty, which lands via an async realm reload. When
        /// <paramref name="markNew"/> is set the set also gets the "new" accent.
        /// </summary>
        public void SelectWhenLoaded(string setIdentity, string? difficultyName = null, bool markNew = false)
        {
            pendingSelectIdentity = setIdentity;
            pendingSelectDiffName = difficultyName;
            if (markNew)
                newIdentities.Add(setIdentity);
            rebuild();
        }

        public void SetFilter(string value)
        {
            filter = value.Trim().ToLowerInvariant();
            rebuild();
        }

        public void SetSort(SortMode value)
        {
            sort = value;
            rebuild();
        }

        public void SelectNext() => moveSelection(1);
        public void SelectPrevious() => moveSelection(-1);

        private void moveSelection(int direction)
        {
            if (entries.Count == 0)
                return;

            int current = selectedCard == null ? -1 : entries.IndexOf(selectedCard);
            int next = current < 0
                ? (direction > 0 ? 0 : entries.Count - 1)
                : Math.Clamp(current + direction, 0, entries.Count - 1);

            select(entries[next]);
        }

        /// <summary>Jumps to the first difficulty of the next set (whole-song step).</summary>
        public void SelectNextSet()
        {
            if (entries.Count == 0)
                return;

            int current = selectedCard == null ? -1 : entries.IndexOf(selectedCard);
            if (current < 0)
            {
                select(entries[0]);
                return;
            }

            string identity = entries[current].Set!.Identity;
            for (int i = current + 1; i < entries.Count; i++)
            {
                if (entries[i].Set!.Identity != identity)
                {
                    select(entries[i]);
                    return;
                }
            }
        }

        /// <summary>Jumps to the first difficulty of the previous set (whole-song step).</summary>
        public void SelectPreviousSet()
        {
            if (entries.Count == 0)
                return;

            int current = selectedCard == null ? -1 : entries.IndexOf(selectedCard);
            if (current < 0)
            {
                select(entries[^1]);
                return;
            }

            string identity = entries[current].Set!.Identity;

            // Walk back to the start of the current set.
            int start = current;
            while (start > 0 && entries[start - 1].Set!.Identity == identity)
                start--;

            if (start == 0)
            {
                select(entries[0]);
                return;
            }

            // Then back to the start of the previous set.
            string previous = entries[start - 1].Set!.Identity;
            int target = start - 1;
            while (target > 0 && entries[target - 1].Set!.Identity == previous)
                target--;

            select(entries[target]);
        }

        /// <summary>Selects (and so plays) a random difficulty, avoiding an immediate repeat.</summary>
        public void SelectRandom()
        {
            if (entries.Count == 0)
                return;

            Card pick;
            if (entries.Count == 1)
            {
                pick = entries[0];
            }
            else
            {
                do
                {
                    pick = entries[random.Next(entries.Count)];
                }
                while (keyOf(pick) == selectedKey);
            }

            select(pick);
        }

        private void rebuild()
        {
            var visible = allSets
                .Where(s => filter.Length == 0 || s.SearchText.Contains(filter, StringComparison.Ordinal))
                .ApplySort(sort)
                .ToList();

            foreach (var card in cards)
                card.Realized?.Expire();

            panelContainer.Clear();
            cards.Clear();
            entries.Clear();

            foreach (var set in visible)
            {
                // Defensive: BeatmapStore already drops sets with no osu! difficulties.
                if (set.Difficulties.Count == 0)
                    continue;

                bool isNew = newIdentities.Contains(set.Identity);

                // Every set - even a single-difficulty one - is laid out as a header card with its
                // difficulty card(s) nested beneath, so single-diff sets behave like multi-diff sets.
                var capturedSet = set;
                var headerCard = new Card(null, null, BeatmapSetPanel.PANEL_HEIGHT, null!);
                cards.Add(headerCard);

                Card? firstDiff = null;

                // Difficulties ordered easiest-to-hardest by star rating.
                foreach (var diff in set.Difficulties.OrderBy(d => d.StarRating))
                {
                    var capturedDiff = diff;
                    Card card = null!;
                    card = new Card(set, diff, BeatmapDiffPanel.PANEL_HEIGHT, () =>
                    {
                        var panel = new BeatmapDiffPanel(capturedSet, capturedDiff, Textures)
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Width = 0.85f, // narrower than the set card, right-aligned so it reads as nested
                            Action = () => clickCard(card),
                            ContextMenuItems = buildMenu(capturedSet, capturedDiff, includeEdit: true, setLevel: false),
                        };
                        return panel;
                    });
                    cards.Add(card);
                    entries.Add(card);
                    firstDiff ??= card;
                }

                // The header's "create" actions use the easiest difficulty as the template/donor.
                var headerTemplate = set.Difficulties.OrderBy(d => d.StarRating).First();

                // Clicking the header selects the set's first difficulty.
                headerCard.Factory = () => new BeatmapSetPanel(capturedSet, isNew, isHeader: true, textures: Textures)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Action = () =>
                    {
                        if (firstDiff != null)
                            select(firstDiff);
                    },
                    // No "Edit" on the header - it isn't a specific difficulty. Deletes the whole set.
                    ContextMenuItems = buildMenu(capturedSet, headerTemplate, includeEdit: false, setLevel: true),
                };
            }

            layoutCards();
            updateEmptyMessage(visible.Count);
            restoreSelection();
            applyPendingSelection();
            updateVisibility();
        }

        /// <summary>
        /// If a <see cref="SelectWhenLoaded"/> target is now present, select + centre it and clear the
        /// request. Otherwise the request is kept so a later rebuild/reload can resolve it.
        /// </summary>
        private void applyPendingSelection()
        {
            if (pendingSelectIdentity == null)
                return;

            Card? target = entries.FirstOrDefault(e =>
                e.Set!.Identity == pendingSelectIdentity
                && (pendingSelectDiffName == null || e.Diff!.DifficultyName == pendingSelectDiffName));

            if (target == null)
                return;

            pendingSelectIdentity = null;
            pendingSelectDiffName = null;
            select(target);
        }

        /// <summary>Assigns each card a vertical position and sizes the scroll content.</summary>
        private void layoutCards()
        {
            float y = padding_top;
            foreach (var card in cards)
            {
                card.Y = y;
                y += card.Height + card_spacing;
            }

            panelContainer.Height = cards.Count == 0 ? 0 : y - card_spacing + padding_bottom;
        }

        protected override void Update()
        {
            base.Update();
            updateVisibility();

            if (!rightScrolling)
                return;

            // Stop if the button was released somewhere we didn't get the up event.
            var inputManager = GetContainingInputManager();
            if (inputManager == null || !inputManager.CurrentState.Mouse.IsPressed(MouseButton.Right))
            {
                rightScrolling = false;
                return;
            }

            scrubTo(inputManager.CurrentState.Mouse.Position);
        }

        // --- osu!lazer-style right-mouse scrub, handled at the carousel's full height so it also works in
        // the gap above the inset scroll box (making it easy to fling straight to the top/bottom). ---

        private bool rightScrolling;

        // Easing rate for the scrub; matches osu!lazer's smooth scrollbar feel.
        private const double scrub_decay = 0.02;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // Scrub on right-drag, but not over a card - those are reserved for the context menu.
            if (e.Button == MouseButton.Right && !isOverCard())
            {
                rightScrolling = true;
                scrubTo(e.ScreenSpaceMousePosition);
                return true;
            }

            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            if (e.Button == MouseButton.Right)
                rightScrolling = false;

            base.OnMouseUp(e);
        }

        private bool isOverCard()
        {
            var inputManager = GetContainingInputManager();
            return inputManager != null && inputManager.HoveredDrawables.Any(d => d is ICarouselPanel);
        }

        /// <summary>Maps the cursor's vertical position (over the scroll viewport) straight to scroll position.</summary>
        private void scrubTo(Vector2 screenSpacePosition)
        {
            // Mapping through the scroll's own local space means cursor positions above the inset scroll box
            // clamp to 0 (top) - so right-clicking the empty strip above the cards flings straight to the top.
            float proportion = Math.Clamp(scroll.ToLocalSpace(screenSpacePosition).Y / scroll.DrawHeight, 0f, 1f);
            scroll.ScrollTo(proportion * scroll.ScrollableExtent, true, scrub_decay);
        }

        /// <summary>Materialises cards within (or near) the viewport and disposes those that left it.</summary>
        private void updateVisibility()
        {
            // Realising uses LoadComponentAsync, which is only valid once we've finished loading.
            // The first Update after load runs this anyway, so the rebuild-time call is just skipped.
            if (!IsLoaded || cards.Count == 0)
                return;

            float viewTop = (float)scroll.Current;
            float viewBottom = viewTop + scroll.DrawHeight;

            // One viewport of slack before realising; two before disposing (hysteresis avoids thrash).
            float realizeMargin = scroll.DrawHeight + 100;
            float disposeMargin = realizeMargin * 2;

            foreach (var card in cards)
            {
                float top = card.Y;
                float bottom = card.Y + card.Height;

                bool shouldRealize = bottom >= viewTop - realizeMargin && top <= viewBottom + realizeMargin;
                bool shouldDispose = bottom < viewTop - disposeMargin || top > viewBottom + disposeMargin;

                if (shouldRealize && card.Realized == null)
                    realize(card);
                else if (shouldDispose && card.Realized != null)
                {
                    card.Realized.Expire();
                    card.Realized = null;
                }
            }
        }

        private void realize(Card card)
        {
            var drawable = card.Factory();
            drawable.Y = card.Y;
            card.Realized = drawable;

            LoadComponentAsync(drawable, loaded =>
            {
                // Discarded again before the async load finished - drop it.
                if (card.Realized != loaded)
                {
                    loaded.Expire();
                    return;
                }

                panelContainer.Add(loaded);

                if (card.Set != null && loaded is ICarouselPanel panel)
                    panel.SetSelected(card == selectedCard);
            });
        }

        private void restoreSelection()
        {
            // Keep the previously-selected difficulty highlighted across filter/sort rebuilds. If it was
            // filtered out, selectedKey is kept so it re-highlights when it reappears.
            selectedCard = selectedKey == null ? null : entries.FirstOrDefault(e => keyOf(e) == selectedKey);
            applySelectionVisuals();
        }

        /// <summary>
        /// Builds the right-click menu for a carousel entry. Edit is omitted on header rows; the delete
        /// entry is "Delete set" on set-level cards and "Delete difficulty" on per-difficulty cards.
        /// </summary>
        private MenuItem[] buildMenu(BeatmapSetModel set, BeatmapDifficultyModel diff, bool includeEdit, bool setLevel)
        {
            var items = new List<MenuItem>();

            if (includeEdit)
                items.Add(new IconMenuItem("Edit", FontAwesome.Solid.Edit, () => EditRequested?.Invoke(set, diff)));

            items.Add(new IconMenuItem("Create new Difficulty", FontAwesome.Solid.Plus, () => CreateDifficultyRequested?.Invoke(set, diff)));
            items.Add(new IconMenuItem("Create new Set", FontAwesome.Solid.FolderPlus, () => CreateSetRequested?.Invoke(set, diff)));
            items.Add(new IconMenuItem("Export .osz", FontAwesome.Solid.FileExport, () => ExportSetRequested?.Invoke(set)));

            if (setLevel)
                items.Add(new IconMenuItem("Delete set", FontAwesome.Solid.TrashAlt, () => DeleteSetRequested?.Invoke(set), destructive: true));
            else
                items.Add(new IconMenuItem("Delete difficulty", FontAwesome.Solid.TrashAlt, () => DeleteDifficultyRequested?.Invoke(set, diff), destructive: true));

            return items.ToArray();
        }

        /// <summary>Click behaviour: first click selects; clicking the already-selected card opens it.</summary>
        private void clickCard(Card card)
        {
            if (card.Set == null || card.Diff == null)
                return;

            if (card == selectedCard)
                EditRequested?.Invoke(card.Set, card.Diff);
            else
                select(card);
        }

        private void select(Card card)
        {
            if (card.Set == null || card.Diff == null)
                return;

            selectedCard = card;
            selectedKey = keyOf(card);

            applySelectionVisuals();
            scrollToCentre(card);
            SelectionChanged?.Invoke(card.Set, card.Diff);
        }

        private void applySelectionVisuals()
        {
            foreach (var card in entries)
            {
                // Skip panels still loading asynchronously - applying a transform before the drawable has
                // a clock throws (NRE in PopulateTransform). realize()'s load callback fixes their selection
                // state once loading completes, so they don't get missed.
                if (card.Realized is { IsLoaded: true } and ICarouselPanel panel)
                    panel.SetSelected(card == selectedCard);
            }
        }

        /// <summary>Scrolls so the given card sits on the vertical centre of the carousel viewport.</summary>
        private void scrollToCentre(Card card)
        {
            float panelCentre = card.Y + card.Height / 2f;
            // Clamp to the scrollable range so selecting an item near the top/bottom doesn't overscroll
            // and bounce back (it just rests against the edge instead).
            float target = (float)Math.Clamp(panelCentre - scroll.DrawHeight / 2f, 0d, scroll.ScrollableExtent);
            scroll.ScrollTo(target);
        }

        private static string keyOf(Card c) => $"{c.Set!.Identity}|{c.Diff!.DifficultyName}";

        private void updateEmptyMessage(int count)
        {
            if (count > 0)
            {
                emptyMessage.FadeOut(150);
                return;
            }

            emptyMessage.Text = allSets.Count == 0
                ? "No osu!lazer beatmaps found."
                : "No beatmaps match your search.";
            emptyMessage.FadeIn(150);
        }

        /// <summary>
        /// Scroll container that adds osu!lazer's "right-mouse scrollbar": holding the right button
        /// scrubs the list by absolute position (the cursor's vertical position maps straight to the
        /// scroll position). The framework doesn't raise drag events for the right button, so this is
        /// driven manually from the mouse-down plus a per-frame position read while held.
        /// </summary>
        private partial class CarouselScrollContainer : BasicScrollContainer
        {
            protected override bool OnScroll(ScrollEvent e)
            {
                // Leave Shift+wheel unconsumed so it bubbles to the global ScrollCatcher (volume control)
                // behind the screen stack; the carousel only handles plain scrolling.
                if (e.ShiftPressed)
                    return false;

                return base.OnScroll(e);
            }
        }

        /// <summary>
        /// A laid-out carousel row. Selectable rows carry a <see cref="Set"/>/<see cref="Diff"/>;
        /// header rows leave them null. The drawable is created lazily via <see cref="Factory"/>.
        /// </summary>
        private sealed class Card
        {
            public readonly BeatmapSetModel? Set;
            public readonly BeatmapDifficultyModel? Diff;
            public readonly float Height;

            public Func<Drawable> Factory;
            public float Y;
            public Drawable? Realized;

            public Card(BeatmapSetModel? set, BeatmapDifficultyModel? diff, float height, Func<Drawable> factory)
            {
                Set = set;
                Diff = diff;
                Height = height;
                Factory = factory;
            }
        }
    }
}
