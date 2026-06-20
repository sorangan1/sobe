using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The timing-points editor (F6): a centred modal listing every timing point in the osu! stable model
    /// (red = uninherited/BPM, green = inherited/SV). Supports multi-selection (click, Ctrl+click to toggle,
    /// Shift+click for a range) so several points can be deleted or edited together. Edits flow through
    /// <see cref="IEditorActions"/>; on save the stable lines are written and osu!lazer's importer translates
    /// them into its per-property control points.
    /// </summary>
    public partial class TimingPointsOverlay : VisibilityContainer
    {
        private readonly ParsedBeatmap beatmap;
        private readonly Func<double> currentTime;

        [Resolved]
        private IEditorActions actions { get; set; } = null!;

        private Container panel = null!;
        private BasicScrollContainer scroll = null!;
        private FillFlowContainer list = null!;
        private Container editPanel = null!;
        private SpriteText countLabel = null!;

        // Fixed row metrics, used to centre the selected point in the list when the panel opens.
        private const float row_height = 34f;
        private float rowStride => row_height + EditorTheme.Spacing.Xs;
        private int selectedRowIndex = -1;

        // Manual double-click tracking (rows are rebuilt on every click, so the framework's own detection can't
        // fire): a second click on the same row within this window counts as a double-click.
        private const double double_click_ms = 300;
        private int lastClickedId = -1;
        private double lastClickMs = double.NegativeInfinity;

        // Multi-selection: the set of selected point ids plus the anchor for Shift+click range selection.
        private readonly HashSet<int> selectedIds = new HashSet<int>();
        private int selectionAnchor = -1;
        private bool focusValueNext;

        /// <summary>Which kind of timing point the list shows: all, only red (BPM) or only green (SV).</summary>
        private enum TimingFilter { All, Bpm, Sv }

        private TimingFilter filter = TimingFilter.All;

        protected override bool StartHidden => true;

        public TimingPointsOverlay(ParsedBeatmap beatmap, Func<double> currentTime)
        {
            this.beatmap = beatmap;
            this.currentTime = currentTime;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            const float header = 50f;
            const float footer = 52f;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new ClickBlocker
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(EditorTheme.Sizing.OverlayWidth, EditorTheme.Sizing.OverlayHeight),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },

                        // --- Header: title + selection count, with a hairline divider below. ---
                        new SpriteText
                        {
                            Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl, Top = EditorTheme.Spacing.Lg },
                            Text = "Timing Points",
                            Colour = EditorTheme.Colours.Accent,
                            Font = EditorTheme.Type.Title(),
                        },
                        countLabel = new SpriteText
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Margin = new MarginPadding { Right = EditorTheme.Spacing.Xl, Top = EditorTheme.Spacing.Lg + 4 },
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Label(),
                        },
                        divider(Anchor.TopLeft, RelativeSizeAxes: Axes.X, y: header, width: 1f),

                        // --- Left: filter tabs above a scrollable list of points. ---
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Padding = new MarginPadding { Top = header + EditorTheme.Spacing.Md, Bottom = footer, Left = EditorTheme.Spacing.Xl, Right = EditorTheme.Spacing.Md },
                            Children = new Drawable[]
                            {
                                new FilterTabs(f => { filter = f; rebuildList(); })
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    RelativeSizeAxes = Axes.X,
                                    Height = 26,
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Top = 26 + EditorTheme.Spacing.Sm },
                                    Child = scroll = new BasicScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        ScrollbarVisible = false,
                                        Child = list = new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                                        },
                                    },
                                },
                            },
                        },

                        // --- Add buttons pinned bottom-left. ---
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl, Bottom = EditorTheme.Spacing.Lg },
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                            Children = new Drawable[]
                            {
                                addButton("+ Red", EditorTheme.Colours.Timing, () => add(true)),
                                addButton("+ Green", EditorTheme.Colours.Velocity, () => add(false)),
                            },
                        },

                        // --- Vertical divider between list and edit panel. ---
                        divider(Anchor.TopCentre, RelativeSizeAxes: Axes.Y, x: 0, width: 1f, topInset: header, bottomInset: 0),

                        // --- Right: edit panel for the current selection. ---
                        editPanel = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Padding = new MarginPadding { Top = header + EditorTheme.Spacing.Lg, Bottom = EditorTheme.Spacing.Lg, Left = EditorTheme.Spacing.Xl, Right = EditorTheme.Spacing.Xl },
                        },
                    },
                },
            };
        }

        /// <summary>A thin neutral divider line, positioned by anchor; used for the header rule and the column split.</summary>
        private static Drawable divider(Anchor anchor, Axes RelativeSizeAxes, float width, float x = 0, float y = 0, float topInset = 0, float bottomInset = 0)
        {
            // For a vertical divider we approximate insets with padding via a wrapping container.
            var line = new Box
            {
                Anchor = anchor,
                Origin = anchor,
                Colour = EditorTheme.Colours.Border,
            };

            if (RelativeSizeAxes == Axes.X)
            {
                line.RelativeSizeAxes = Axes.X;
                line.Height = width;
                line.Y = y;
                return line;
            }

            // Vertical: wrap so we can inset top/bottom while keeping it centred on the column split.
            return new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.Y,
                Width = width,
                Padding = new MarginPadding { Top = topInset, Bottom = bottomInset },
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Border },
            };
        }

        private static OsuButton addButton(string text, Color4 typeColour, Action action) =>
            new OsuButton(text, typeColour) { Size = new Vector2(96, 30), FontSize = 13, Action = action };

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, 400, EditorTheme.Motion.Ease);
            selectAtCurrentTime();
            rebuildList();
            centreOnSelection();
        }

        /// <summary>Scrolls the list so the selected point sits in the vertical centre of the viewport, on open.</summary>
        private void centreOnSelection()
        {
            if (selectedRowIndex < 0)
                return;

            int index = selectedRowIndex;
            // Defer until the freshly built rows have been laid out so the scroll extent + viewport are valid.
            ScheduleAfterChildren(() =>
            {
                float rowCentre = index * rowStride + row_height / 2f;
                scroll.ScrollTo(rowCentre - scroll.DrawHeight / 2f, false); // ScrollContainer clamps the target
            });
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.97f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        /// <summary>
        /// Selects the timing point active at the playhead when the menu opens. If a point sits right under the
        /// playhead it wins (green beats red when both share the tick); otherwise the most recent point before
        /// the playhead is selected, so opening the panel between two ticks picks the one currently in force.
        /// </summary>
        private void selectAtCurrentTime()
        {
            const double tolerance = 2; // ms; the playhead is normally beat-snapped onto the point's time

            double now = currentTime();

            // First, a point right under the playhead (green wins over red when they share the tick); otherwise
            // the most recent point at or before now; if none precedes, the earliest point overall.
            var under = beatmap.TimingPointModels
                .Where(t => Math.Abs(t.Time - now) <= tolerance)
                .OrderBy(t => t.Uninherited)
                .ToList();

            var before = beatmap.TimingPointModels
                .Where(t => t.Time <= now)
                .OrderByDescending(t => t.Time)
                .ToList();

            var all = beatmap.TimingPointModels.OrderBy(t => t.Time).ToList();

            var chosen = under.Count > 0 ? under : before.Count > 0 ? before : all;

            selectedIds.Clear();
            if (chosen.Count > 0)
            {
                selectedIds.Add(chosen[0].Id);
                selectionAnchor = chosen[0].Id;
            }
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

            // Ctrl+A selects every point in the active tab (All / BPM / SV) — not literally every point,
            // since the user only sees the filtered list. Delete removes the current selection.
            if (e.Key == Key.A && Shortcut.CommandPressed(e))
            {
                selectedIds.Clear();
                foreach (var tp in beatmap.TimingPointModels)
                    if (matchesFilter(tp))
                        selectedIds.Add(tp.Id);
                rebuildList();
                return true;
            }

            if ((e.Key == Key.Delete || e.Key == Key.BackSpace) && selectedIds.Count > 0)
            {
                deleteSelected();
                return true;
            }

            return base.OnKeyDown(e);
        }

        // --- Selection handling (single / Ctrl-toggle / Shift-range), mirroring the editor's selection model. ---

        private List<int> orderedIds() => beatmap.TimingPointModels.OrderBy(t => t.Time).Select(t => t.Id).ToList();

        /// <summary>Whether a point passes the active (All / BPM / SV) list filter.</summary>
        private bool matchesFilter(TimingPointModel tp) => filter switch
        {
            TimingFilter.Bpm => tp.Uninherited,
            TimingFilter.Sv => !tp.Uninherited,
            _ => true,
        };

        /// <summary>Whether this click on a row is the second of a double-click (same row, within the window).</summary>
        private bool isDoubleClick(int id)
        {
            double now = Clock.CurrentTime;
            bool dbl = id == lastClickedId && now - lastClickMs <= double_click_ms;
            lastClickedId = id;
            // Reset the timer after a double-click so a rapid third click doesn't chain into another one.
            lastClickMs = dbl ? double.NegativeInfinity : now;
            return dbl;
        }

        private void rowClicked(int id, bool ctrl, bool shift)
        {
            if (shift && selectionAnchor != -1)
            {
                var ordered = orderedIds();
                int a = ordered.IndexOf(selectionAnchor);
                int b = ordered.IndexOf(id);
                if (a >= 0 && b >= 0)
                {
                    if (!ctrl)
                        selectedIds.Clear();
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
                        selectedIds.Add(ordered[i]);
                }
            }
            else if (ctrl)
            {
                if (!selectedIds.Add(id))
                    selectedIds.Remove(id);
                selectionAnchor = id;
            }
            else
            {
                selectedIds.Clear();
                selectedIds.Add(id);
                selectionAnchor = id;
            }

            rebuildList();
        }

        private void add(bool uninherited)
        {
            double time = Math.Round(currentTime());

            double beatLength = uninherited
                ? activeBeatLength(time)
                : TimingPointLineEditor.BeatLengthFromSv(1.0);

            var point = new TimingPointModel(
                Id: -1,
                Time: time,
                BeatLength: beatLength,
                Meter: 4,
                SampleSet: 0,
                SampleIndex: 0,
                Volume: 100,
                Uninherited: uninherited,
                Effects: 0);

            int id = actions.AddTimingPoint(point);
            selectedIds.Clear();
            selectedIds.Add(id);
            selectionAnchor = id;
            focusValueNext = true; // auto-focus the BPM / SV field so the mapper can type right away
            rebuildList();
        }

        /// <summary>The beat length (ms/beat) of the uninherited point in force at the given time.</summary>
        private double activeBeatLength(double time)
        {
            double beatLength = 500; // 120 BPM fallback
            foreach (var tp in beatmap.TimingPointModels.Where(t => t.Uninherited).OrderBy(t => t.Time))
            {
                if (tp.Time > time)
                    break;
                beatLength = tp.BeatLength;
            }
            return beatLength;
        }

        private void deleteSelected()
        {
            if (selectedIds.Count == 0)
                return;

            actions.DeleteTimingPoints(selectedIds.ToList());
            selectedIds.Clear();
            selectionAnchor = -1;
            rebuildList();
        }

        private void rebuildList()
        {
            list.Clear();

            var shown = beatmap.TimingPointModels.OrderBy(t => t.Time).Where(matchesFilter).ToList();

            foreach (var tp in shown)
            {
                int id = tp.Id;
                double time = tp.Time;
                list.Add(new TimingRow(tp, selectedIds.Contains(id))
                {
                    Clicked = (ctrl, shift) =>
                    {
                        // The list is rebuilt on every click (replacing the row drawables), so a real
                        // double-click never reaches a single row - detect it here by id + time instead.
                        if (isDoubleClick(id))
                            actions.SeekTo(time);
                        rowClicked(id, ctrl, shift);
                    },
                });
            }

            // Track which visible row is selected so the panel can centre it on open (the anchor wins; else
            // the first selected row that the active filter still shows).
            selectedRowIndex = -1;
            if (selectedIds.Count > 0)
            {
                int idx = shown.FindIndex(tp => tp.Id == selectionAnchor);
                if (idx < 0)
                    idx = shown.FindIndex(tp => selectedIds.Contains(tp.Id));
                selectedRowIndex = idx;
            }

            int total = beatmap.TimingPointModels.Count;
            int visible = shown.Count;
            countLabel.Text = selectedIds.Count > 1
                ? $"{selectedIds.Count} of {total} selected"
                : filter == TimingFilter.All
                    ? $"{total} point{(total == 1 ? "" : "s")}"
                    : $"{visible} of {total} point{(total == 1 ? "" : "s")}";

            rebuildEditPanel();
        }

        private void rebuildEditPanel()
        {
            var selected = beatmap.TimingPointModels.Where(t => selectedIds.Contains(t.Id)).OrderBy(t => t.Time).ToList();

            if (selected.Count == 0)
                editPanel.Child = hint($"Select a timing point to edit.\n{Shortcut.CommandName}+click to add, Shift+click for a range.");
            else if (selected.Count == 1)
                editPanel.Child = buildSingleEdit(selected[0]);
            else
                editPanel.Child = buildMultiEdit(selected);
        }

        private static Drawable hint(string text) => new SpriteText
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Body(),
        };

        private static SpriteText sectionHeader(string text, Color4 colour) => new SpriteText
        {
            Text = text,
            Colour = colour,
            Font = EditorTheme.Type.Heading(),
        };

        private Drawable buildSingleEdit(TimingPointModel tp)
        {
            bool red = tp.Uninherited;

            var timeBox = labelledBox("Time (ms)", tp.Time.ToString("0", CultureInfo.InvariantCulture));
            var valueBox = labelledBox(red ? "BPM" : "SV multiplier",
                (red ? tp.Bpm : tp.SliderVelocity).ToString("0.###", CultureInfo.InvariantCulture), focusValueNext);
            focusValueNext = false;
            var meterBox = red ? labelledBox("Meter (beats/bar)", tp.Meter.ToString(CultureInfo.InvariantCulture)) : null;
            // Hitsound volume (0-100), the green line's main job alongside SV; editable on red lines too.
            var volumeBox = labelledBox("Hitsound volume (%)", tp.Volume.ToString(CultureInfo.InvariantCulture));

            bool kiai = tp.Kiai;
            var kiaiToggle = toggleButton(kiai, on => { kiai = on; apply(); });

            void apply()
            {
                if (!double.TryParse(timeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double newTime))
                    newTime = tp.Time;
                double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double newValue);

                double beatLength = red
                    ? TimingPointLineEditor.BeatLengthFromBpm(newValue)
                    : TimingPointLineEditor.BeatLengthFromSv(Math.Clamp(newValue, 0.1, 10));

                int meter = tp.Meter;
                if (red && meterBox != null && int.TryParse(meterBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0)
                    meter = m;

                int volume = tp.Volume;
                if (int.TryParse(volumeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vol))
                    volume = Math.Clamp(vol, 0, 100);

                actions.UpdateTimingPoint(tp with
                {
                    Time = newTime,
                    BeatLength = beatLength,
                    Meter = meter,
                    Volume = volume,
                    Effects = TimingPointLineEditor.WithKiai(tp.Effects, kiai),
                });
                rebuildList();
            }

            // Pressing Enter in any field commits the edit.
            timeBox.Box.OnCommit += (_, _) => apply();
            valueBox.Box.OnCommit += (_, _) => apply();
            volumeBox.Box.OnCommit += (_, _) => apply();
            if (meterBox != null)
                meterBox.Box.OnCommit += (_, _) => apply();

            var rows = new List<Drawable>
            {
                sectionHeader(red ? "Red line — BPM / timing" : "Green line — slider velocity",
                    red ? EditorTheme.Colours.Timing : EditorTheme.Colours.Velocity),
                timeBox.Container,
                valueBox.Container,
            };

            if (meterBox != null)
                rows.Add(meterBox.Container);

            rows.Add(volumeBox.Container);

            rows.Add(fieldLabel("Kiai"));
            rows.Add(kiaiToggle);
            rows.Add(actionRow(
                new OsuButton("Apply", EditorTheme.Colours.Accent) { Size = new Vector2(104, EditorTheme.Sizing.ButtonHeight), FontSize = 13, Action = apply },
                new OsuButton("Delete", EditorTheme.Colours.Error)
                {
                    Size = new Vector2(104, EditorTheme.Sizing.ButtonHeight),
                    FontSize = 13,
                    Action = deleteSelected,
                }));

            return editColumn(rows);
        }

        private Drawable buildMultiEdit(List<TimingPointModel> selected)
        {
            bool allRed = selected.All(t => t.Uninherited);
            bool allGreen = selected.All(t => !t.Uninherited);

            var rows = new List<Drawable>
            {
                sectionHeader($"{selected.Count} points selected",
                    allRed ? EditorTheme.Colours.Timing : allGreen ? EditorTheme.Colours.Velocity : EditorTheme.Colours.Text),
            };

            // A value field is only meaningful when the whole selection is one type (all BPM or all SV).
            if (allRed || allGreen)
            {
                var valueBox = labelledBox(allRed ? "Set BPM (all)" : "Set SV multiplier (all)", string.Empty);

                void applyValue()
                {
                    if (!double.TryParse(valueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        return;

                    double beatLength = allRed
                        ? TimingPointLineEditor.BeatLengthFromBpm(v)
                        : TimingPointLineEditor.BeatLengthFromSv(Math.Clamp(v, 0.1, 10));

                    var updated = selected.Select(tp => tp with { BeatLength = beatLength }).ToList();
                    actions.UpdateTimingPoints(updated);
                    rebuildList();
                }

                valueBox.Box.OnCommit += (_, _) => applyValue();
                rows.Add(valueBox.Container);
                rows.Add(new OsuButton(allRed ? "Apply BPM to all" : "Apply SV to all", EditorTheme.Colours.Accent)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = EditorTheme.Sizing.ButtonHeight,
                    FontSize = 13,
                    Action = applyValue,
                });
            }
            else
            {
                rows.Add(hint("Mixed red & green selected —\nvalue editing is per-type."));
            }

            // Hitsound volume applies to any selection (red or green).
            var volumeBox = labelledBox("Set volume % (all)", string.Empty);

            void applyVolume()
            {
                if (!int.TryParse(volumeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    return;

                int volume = Math.Clamp(v, 0, 100);
                var updated = selected.Select(tp => tp with { Volume = volume }).ToList();
                actions.UpdateTimingPoints(updated);
                rebuildList();
            }

            volumeBox.Box.OnCommit += (_, _) => applyVolume();
            rows.Add(volumeBox.Container);
            rows.Add(new OsuButton("Apply volume to all", EditorTheme.Colours.Accent)
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.ButtonHeight,
                FontSize = 13,
                Action = applyVolume,
            });

            // Kiai applies to any selection.
            rows.Add(fieldLabel("Kiai (all selected)"));
            rows.Add(actionRow(
                new OsuButton("Kiai ON", EditorTheme.Colours.Accent) { Size = new Vector2(104, EditorTheme.Sizing.ButtonHeight), FontSize = 13, Action = () => setKiaiAll(selected, true) },
                new OsuButton("Kiai OFF", EditorTheme.Colours.Control) { Size = new Vector2(104, EditorTheme.Sizing.ButtonHeight), FontSize = 13, Action = () => setKiaiAll(selected, false) }));

            rows.Add(new OsuButton($"Delete {selected.Count} points", EditorTheme.Colours.Error)
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.ButtonHeight,
                FontSize = 13,
                Action = deleteSelected,
            });

            return editColumn(rows);
        }

        private void setKiaiAll(List<TimingPointModel> selected, bool on)
        {
            var updated = selected.Select(tp => tp with { Effects = TimingPointLineEditor.WithKiai(tp.Effects, on) }).ToList();
            actions.UpdateTimingPoints(updated);
            rebuildList();
        }

        private static Drawable editColumn(List<Drawable> rows) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
            Children = rows.ToArray(),
        };

        private static Drawable actionRow(params Drawable[] buttons) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
            Children = buttons,
        };

        private static SpriteText fieldLabel(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Label(),
        };

        /// <summary>A small toggle styled like the design-system button; shows the current on/off state.</summary>
        private static OsuButton toggleButton(bool on, Action<bool> onToggle)
        {
            bool state = on;
            var button = new OsuButton(state ? "On" : "Off", state ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control)
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.ButtonHeight,
                FontSize = 13,
            };
            button.Action = () => onToggle(!state);
            return button;
        }

        /// <summary>A labelled text box; returns the container to add plus the live box to read on apply.</summary>
        private LabelledBox labelledBox(string label, string initial, bool autoFocus = false)
        {
            var box = new FocusTextBox(autoFocus) { RelativeSizeAxes = Axes.X, Height = EditorTheme.Sizing.InputHeight, Text = initial };
            var container = (Drawable)new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                Children = new Drawable[]
                {
                    fieldLabel(label),
                    box,
                },
            };
            return new LabelledBox(container, box);
        }

        private sealed class LabelledBox
        {
            public readonly Drawable Container;
            public readonly BasicTextBox Box;

            public LabelledBox(Drawable container, BasicTextBox box)
            {
                Container = container;
                Box = box;
            }

            public string Text => Box.Text;
        }

        /// <summary>A clickable row representing one timing point in the list, with hover + multi-select states.</summary>
        private partial class TimingRow : Container
        {
            public Action<bool, bool>? Clicked;

            // Fixed right-hand columns so volume and value stay vertically aligned across rows.
            private const float value_column_width = 86f;
            private const float volume_column_width = 46f;
            private const float column_gap = 10f;

            private readonly TimingPointModel point;
            private readonly bool selected;
            private Box background = null!;

            public TimingRow(TimingPointModel point, bool selected)
            {
                this.point = point;
                this.selected = selected;
                RelativeSizeAxes = Axes.X;
                Height = 34;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                bool red = point.Uninherited;
                Color4 accent = red ? EditorTheme.Colours.Timing : EditorTheme.Colours.Velocity;
                string ts = TimeSpanLabel(point.Time);
                string value = red
                    ? point.Bpm.ToString("0.##", CultureInfo.InvariantCulture) + " BPM"
                    : point.SliderVelocity.ToString("0.##", CultureInfo.InvariantCulture) + "x";

                if (selected)
                {
                    BorderThickness = 2;
                    BorderColour = EditorTheme.Colours.Selection;
                }

                // Left group: a type-coloured dot, the time, and (if kiai) a small pill. Anchored to the left
                // edge so it grows rightward, never disturbing the right-aligned value group.
                var leftGroup = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 12 },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new Circle
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(11),
                            Colour = accent,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = ts,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.BodyStrong(numeric: true),
                        },
                    },
                };

                if (point.Kiai)
                    leftGroup.Add(kiaiPill());

                // Value (BPM / SV) lives in a fixed-width column pinned to the right edge, right-aligned so the
                // numbers line up regardless of how wide they are.
                var valueColumn = new Container
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 12 },
                    RelativeSizeAxes = Axes.Y,
                    Width = value_column_width,
                    Child = new SpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Text = value,
                        Colour = accent,
                        Font = EditorTheme.Type.BodyStrong(numeric: true),
                    },
                };

                // Hitsound volume gets its own column just left of the value, so it always sits in the same
                // place across rows instead of floating next to a variable-width value.
                var volumeColumn = new Container
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 12 + value_column_width + column_gap },
                    RelativeSizeAxes = Axes.Y,
                    Width = volume_column_width,
                    Child = new SpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Text = point.Volume.ToString(CultureInfo.InvariantCulture) + "%",
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Body(numeric: true),
                    },
                };

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = selected ? EditorTheme.Colours.Control : EditorTheme.Colours.Raised },
                    leftGroup,
                    volumeColumn,
                    valueColumn,
                };
            }

            /// <summary>A small "KIAI" pill shown beside the time when the point starts a kiai section.</summary>
            private static Drawable kiaiPill() => new CircularContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Sm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Kiai },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 6, Vertical = 1.5f },
                        Text = "KIAI",
                        Colour = EditorTheme.Colours.Sunken,
                        Font = EditorTheme.Type.Caption(),
                    },
                },
            };

            protected override bool OnClick(ClickEvent e)
            {
                Clicked?.Invoke(Shortcut.CommandPressed(e), e.ShiftPressed);
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                    background.FadeColour(EditorTheme.Colours.Raised, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }

            private static string TimeSpanLabel(double ms)
            {
                var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
                return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";
            }
        }

        /// <summary>A text box that grabs keyboard focus when it loads, if asked to.</summary>
        private partial class FocusTextBox : BasicTextBox
        {
            private readonly bool autoFocus;

            public FocusTextBox(bool autoFocus)
            {
                this.autoFocus = autoFocus;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (autoFocus)
                    Schedule(() => GetContainingFocusManager()?.ChangeFocus(this));
            }
        }

        /// <summary>A compact three-way segmented filter (All / BPM / SV) above the timing-point list.</summary>
        private partial class FilterTabs : CompositeDrawable
        {
            private static readonly (string Label, TimingFilter Value)[] options =
            {
                ("All", TimingFilter.All),
                ("BPM", TimingFilter.Bpm),
                ("SV", TimingFilter.Sv),
            };

            private readonly Segment[] segments;

            public FilterTabs(Action<TimingFilter> onChanged)
            {
                var flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                };

                segments = new Segment[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    int index = i;
                    var seg = new Segment(options[i].Label, () =>
                    {
                        select(index);
                        onChanged(options[index].Value);
                    })
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 1f / options.Length,
                    };
                    segments[i] = seg;
                    flow.Add(seg);
                }

                InternalChild = flow;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                select(0);
            }

            private void select(int index)
            {
                for (int i = 0; i < segments.Length; i++)
                    segments[i].SetActive(i == index);
            }

            private partial class Segment : ClickableContainer
            {
                private Box background = null!;
                private SpriteText text = null!;
                private readonly string label;
                private bool active;

                public Segment(string label, Action onClick)
                {
                    this.label = label;
                    Action = onClick;
                    Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Xxs };
                }

                [BackgroundDependencyLoader]
                private void load()
                {
                    InternalChild = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = EditorTheme.Radius.Md,
                        Children = new Drawable[]
                        {
                            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                            text = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = label,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Label(),
                            },
                        },
                    };
                }

                public void SetActive(bool value)
                {
                    active = value;
                    background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    text.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                }

                protected override bool OnHover(HoverEvent e)
                {
                    if (!active)
                        background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e)
                {
                    if (!active)
                        background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }
            }
        }

        /// <summary>Blocks clicks from reaching the dimmed background that dismisses the overlay.</summary>
        private partial class ClickBlocker : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
