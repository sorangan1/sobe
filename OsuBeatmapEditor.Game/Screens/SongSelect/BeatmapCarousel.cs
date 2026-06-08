using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A right-aligned, scrollable list of beatmap sets with live search filtering and sorting.
    /// Single-difficulty sets are one selectable card; multi-difficulty sets show a header card
    /// with smaller, individually-selectable difficulty cards beneath. Selection (yellow border)
    /// is separate from opening the editor.
    /// </summary>
    public partial class BeatmapCarousel : Container
    {
        /// <summary>Invoked when the selected difficulty changes.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? SelectionChanged;

        /// <summary>Invoked when a difficulty's "Edit" context-menu item is chosen.</summary>
        public Action<BeatmapSetModel, BeatmapDifficultyModel>? EditRequested;

        /// <summary>Texture store used to load card background images. Set before <see cref="SetBeatmaps"/>.</summary>
        public LargeTextureStore? Textures { get; set; }

        private readonly BasicScrollContainer scroll;
        private readonly FillFlowContainer panelFlow;
        private readonly SpriteText emptyMessage;

        private readonly List<BeatmapSetModel> allSets = new List<BeatmapSetModel>();
        private readonly HashSet<string> newIdentities = new HashSet<string>();

        private readonly List<Entry> entries = new List<Entry>();
        private int selectedIndex = -1;
        private string? selectedKey;

        private string filter = string.Empty;
        private SortMode sort = SortMode.Artist;

        public bool HasSelection => selectedIndex >= 0;

        public BeatmapCarousel()
        {
            InternalChildren = new Drawable[]
            {
                scroll = new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = panelFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Vertical = 8, Left = 20 },
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

        /// <summary>Adds a freshly-created beatmap to the top of the list, flagged "new" and selected.</summary>
        public void AddNewBeatmap(BeatmapSetModel set)
        {
            allSets.Insert(0, set);
            newIdentities.Add(set.Identity);
            rebuild();

            int index = entries.FindIndex(e => e.Set.Identity == set.Identity);
            if (index >= 0)
                select(index);
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

            int next = selectedIndex < 0
                ? (direction > 0 ? 0 : entries.Count - 1)
                : Math.Clamp(selectedIndex + direction, 0, entries.Count - 1);

            select(next);
        }

        /// <summary>Jumps to the first difficulty of the next set (whole-song step).</summary>
        public void SelectNextSet()
        {
            if (entries.Count == 0)
                return;

            if (selectedIndex < 0)
            {
                select(0);
                return;
            }

            string current = entries[selectedIndex].Set.Identity;
            for (int i = selectedIndex + 1; i < entries.Count; i++)
            {
                if (entries[i].Set.Identity != current)
                {
                    select(i);
                    return;
                }
            }
        }

        /// <summary>Jumps to the first difficulty of the previous set (whole-song step).</summary>
        public void SelectPreviousSet()
        {
            if (entries.Count == 0)
                return;

            if (selectedIndex < 0)
            {
                select(entries.Count - 1);
                return;
            }

            string current = entries[selectedIndex].Set.Identity;

            // Walk back to the start of the current set.
            int start = selectedIndex;
            while (start > 0 && entries[start - 1].Set.Identity == current)
                start--;

            if (start == 0)
            {
                select(0);
                return;
            }

            // Then back to the start of the previous set.
            string previous = entries[start - 1].Set.Identity;
            int target = start - 1;
            while (target > 0 && entries[target - 1].Set.Identity == previous)
                target--;

            select(target);
        }

        private void rebuild()
        {
            var visible = allSets
                .Where(s => filter.Length == 0 || s.SearchText.Contains(filter, StringComparison.Ordinal))
                .ApplySort(sort)
                .ToList();

            panelFlow.Clear();
            entries.Clear();

            foreach (var set in visible)
            {
                bool isNew = newIdentities.Contains(set.Identity);

                if (set.Difficulties.Count <= 1)
                {
                    var diff = set.Difficulties.Count == 1 ? set.Difficulties[0] : new BeatmapDifficultyModel();
                    var panel = new BeatmapSetPanel(set, isNew, textures: Textures)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        EditAction = () => EditRequested?.Invoke(set, diff),
                    };
                    addEntry(set, diff, panel, panel.SetSelected);
                }
                else
                {
                    var header = new BeatmapSetPanel(set, isNew, isHeader: true, textures: Textures)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                    };
                    // Clicking the header selects the set's first difficulty.
                    int firstDiffIndex = entries.Count;
                    header.Action = () => select(firstDiffIndex);
                    panelFlow.Add(header);

                    // Difficulties ordered easiest-to-hardest by star rating.
                    foreach (var diff in set.Difficulties.OrderBy(d => d.StarRating))
                    {
                        var captured = diff;
                        var panel = new BeatmapDiffPanel(set, diff, Textures)
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Width = 0.85f, // narrower than the set card, right-aligned so it reads as nested
                            EditAction = () => EditRequested?.Invoke(set, captured),
                        };
                        addEntry(set, diff, panel, panel.SetSelected);
                    }
                }
            }

            updateEmptyMessage(visible.Count);
            restoreSelection();
        }

        private void addEntry(BeatmapSetModel set, BeatmapDifficultyModel diff, ClickableContainer panel, Action<bool> setSelected)
        {
            int index = entries.Count;
            panel.Action = () => select(index);
            panelFlow.Add(panel);
            entries.Add(new Entry(set, diff, panel, setSelected));
        }

        private void restoreSelection()
        {
            // Keep the previously-selected difficulty highlighted across filter/sort rebuilds.
            selectedIndex = selectedKey == null ? -1 : entries.FindIndex(e => keyOf(e) == selectedKey);
            for (int i = 0; i < entries.Count; i++)
                entries[i].SetSelected(i == selectedIndex);
        }

        private void select(int index)
        {
            if (index < 0 || index >= entries.Count)
                return;

            selectedIndex = index;
            selectedKey = keyOf(entries[index]);

            for (int i = 0; i < entries.Count; i++)
                entries[i].SetSelected(i == index);

            scrollToCentre(entries[index].Panel);
            SelectionChanged?.Invoke(entries[index].Set, entries[index].Diff);
        }

        /// <summary>Scrolls so the given panel sits on the vertical centre of the carousel viewport.</summary>
        private void scrollToCentre(Drawable panel)
        {
            double panelCentre = scroll.GetChildPosInContent(panel) + panel.DrawHeight / 2f;
            scroll.ScrollTo(panelCentre - scroll.DrawHeight / 2f);
        }

        private static string keyOf(Entry e) => $"{e.Set.Identity}|{e.Diff.DifficultyName}";

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

        private readonly record struct Entry(BeatmapSetModel Set, BeatmapDifficultyModel Diff, Drawable Panel, Action<bool> SetSelected);
    }
}
