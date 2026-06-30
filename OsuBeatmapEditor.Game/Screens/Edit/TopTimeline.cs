using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Utils;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The zoomed-in editing timeline at the top of the editor: a beat-snapped grid from the map's timing
    /// points, hit-object markers laid out by time, and a fixed centre playhead the content scrolls under.
    /// ALT+scroll zooms. Click selects an object (CTRL toggles); dragging in empty space rubber-band selects
    /// objects within the drag box - mirroring osu!lazer's blueprint/drag-box selection model.
    /// </summary>
    public partial class TopTimeline : CompositeDrawable
    {
        public const float HEIGHT = 76;

        private const float min_px_per_ms = 0.1f;
        private const float max_px_per_ms = 1.5f;

        // The baseline objects rest on (like a table top); ticks hang below it, objects sit above.
        private const float baseline_y = HEIGHT * 0.66f;
        private const float object_size = HEIGHT * 0.5f;
        private const float object_centre_y = baseline_y - object_size / 2f;

        private readonly ParsedBeatmap beatmap;
        private readonly Func<double> currentTime;
        private readonly double trackLength;
        private readonly BindableBool hitsoundMode;

        [Resolved]
        private EditorSelection selection { get; set; } = null!;

        [Resolved]
        private NodeSelection nodeSelection { get; set; } = null!;

        [Resolved]
        private IEditorActions actions { get; set; } = null!;

        /// <summary>Raised when a timing-point pill is clicked: the point's id and the pill's bottom screen position.</summary>
        public Action<int, Vector2>? TimingPillClicked;

        /// <summary>The timing point's sample volume (0..1) at a time, so a hitsound cell can dim by its effective volume.</summary>
        public Func<double, float>? TimingVolume;

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private EditableBeatmap editable { get; set; } = null!;

        [Resolved]
        private BeatSnapDivisor beatSnap { get; set; } = null!;

        /// <summary>The active beat-snap resolution (ticks per beat).</summary>
        private int divisor => beatSnap.Value.Value;

        private float pixelsPerMs = 0.4f;
        // The zoom we ease toward. Scroll sets it instantly (and snaps pixelsPerMs to match); the hitsound
        // min-zoom enforcement (panel open / divisor change) only sets the target so the change animates.
        private float targetPixelsPerMs = 0.4f;

        // Move-drag state (dragging an object shifts the selection in time).
        private bool moving;
        private int moveGrabbedId;
        private double moveStartTime;

        // Slider repeat-drag state (dragging a slider's tail changes its reverse count, like osu!lazer).
        private bool repeatDragging;

        // Slider velocity-drag state (Shift + dragging the tail changes its speed instead of adding reverses).
        private bool velocityDragging;

        // Spinner resize state (dragging a spinner's tail changes its end time / duration).
        private bool spinnerResizing;

        // Hitsound-lane paint state: while a stroke is held, drag applies the same on/off to every column
        // it crosses (lazer-less, but the natural way to hitsound a stream in one gesture).
        private bool hitsoundPainting;
        private bool paintTurnOn;
        private int paintLaneIndex;
        private bool paintFirstApplied;
        private bool hitsoundDragged; // a lane drag happened this press, so the mouse-up shouldn't single-click
        private readonly HashSet<(int ObjectId, int NodeIndex)> paintedColumns = new HashSet<(int, int)>();

        private Container content = null!;
        private Container breakLayer = null!;
        private Container timingLayer = null!;
        private Container gridLayer = null!;
        private Container objectLayer = null!;
        private Container modLayer = null!;
        private Container selectionLayer = null!;
        private Container annotationRangeLayer = null!;
        private Container previewLayer = null!;
        private readonly List<StrokeRangeBar> strokeRangeBars = new List<StrokeRangeBar>();
        private readonly List<Box> gridPool = new List<Box>();

        // Shift-hover "add SV point" affordance: a gray pill below the nearest tick in the timing-pill band.
        private Container svHoverPill = null!;

        // Modding-mode bubbles: the currently-shown (already-filtered) discussions with an in-song timestamp.
        private IReadOnlyList<Online.ModdingDiscussion> modDiscussions = System.Array.Empty<Online.ModdingDiscussion>();
        /// <summary>Invoked when a discussion bubble is clicked (passes its timestamp in ms).</summary>
        public Action<double>? ModBubbleClicked;

        /// <summary>Invoked when the user selects an object via the timeline, so the editor can switch to the select tool.</summary>
        public Action? ObjectSelectedHere;

        // --- Hitsound lanes (the expanded Clap/Whistle/Finish editor) ---
        private const int hs_whistle = 0b0010, hs_finish = 0b0100, hs_clap = 0b1000;

        /// <summary>The three addition lanes, top to bottom, with their hitSound bit and accent colour.</summary>
        private static readonly (string Label, int Bit, Color4 Colour)[] hitsoundLaneDefs =
        {
            ("CLAP", hs_clap, EditorTheme.Colours.Velocity),    // green
            ("WHISTLE", hs_whistle, EditorTheme.Colours.Bookmark), // blue
            ("FINISH", hs_finish, EditorTheme.Colours.Kiai),    // orange
        };

        // Hitsound-lane cell visuals + a slim fixed gutter on the left carrying the C/W/F lane-colour tags. The actual
        // hitsound CONTROLS live in a separate left-side block in the editor (not in the timeline), so the lanes keep
        // (almost) the full timeline width for cells.
        private const float cell_size = 28f;
        private const float lane_gutter = 34f;      // slim: just the lane-tag strip
        private const float lane_tag_width = 24f;   // the strip carrying the C/W/F lane-colour tags

        // Band tints/separators (fixed, drawn behind the cells); the scrolling cells; the left tag gutter (on top).
        private Container laneChrome = null!;
        private Container laneCellsRoot = null!;
        private Box laneColumnHighlight = null!;
        private Container laneLabels = null!;
        private Container laneEffects = null!;
        private readonly Container[] laneCellContainers = new Container[3];

        // Selection state, mirroring lazer: a shared selection + a live drag box.
        private readonly List<ObjBounds> objectBounds = new List<ObjBounds>();
        private readonly Dictionary<int, Container> blueprints = new Dictionary<int, Container>();
        private readonly Dictionary<int, float> blueprintBaseX = new Dictionary<int, float>();

        // Virtualised timing lines + bookmarks: the lightweight index (sorted by time) is rebuilt only when the
        // timing/bookmark data changes; the heavy drawables (line + pill, or bookmark line) are realised on demand
        // for the visible window, so an SV-heavy map stays cheap to zoom/scroll.
        private readonly List<TimingEntry> timingEntries = new List<TimingEntry>();
        private readonly Dictionary<int, Drawable[]> realizedTiming = new Dictionary<int, Drawable[]>();
        private readonly List<int> _toDerealizeTiming = new List<int>();

        // Virtualisation: only objects whose time falls in (or near) the visible window get a realised drawable,
        // so a map with thousands of objects stays cheap to zoom/scroll. objectBounds keeps ALL objects (cheap
        // structs) for hit-testing; the heavy blueprints + hitsound cells are realised/derealised on demand.
        private readonly HashSet<int> realizedIds = new HashSet<int>();
        private readonly Dictionary<int, int> objectIndexById = new Dictionary<int, int>();
        private readonly Dictionary<int, List<Drawable>> objectCells = new Dictionary<int, List<Drawable>>();
        private double maxObjectDuration;
        // The circle markers (head/tail) per object, so selection can recolour their borders + glow.
        private readonly Dictionary<int, List<CircularContainer>> blueprintCircles = new Dictionary<int, List<CircularContainer>>();
        // Per slider, its selectable node circles keyed by node index (0 = head, Slides = tail), for red node selection.
        private readonly Dictionary<int, Dictionary<int, CircularContainer>> sliderNodeCircles = new Dictionary<int, Dictionary<int, CircularContainer>>();
        // Per slider, its body bar plus the colour it rests at, so a body-part selection can tint it yellow.
        private readonly Dictionary<int, (Drawable Bar, Color4 RestColour)> sliderBars = new Dictionary<int, (Drawable, Color4)>();
        private Box? dragBox;
        private double dragStartTime;
        private HashSet<int> dragBaseline = new HashSet<int>();
        // Last cursor screen position during a box drag, so the selection can keep updating while the song
        // plays (the timeline scrolls under a stationary cursor) without needing the mouse to move.
        private Vector2 lastDragScreenPos;

        public TopTimeline(ParsedBeatmap beatmap, Func<double> currentTime, double trackLength, BindableBool hitsoundMode)
        {
            this.beatmap = beatmap;
            this.currentTime = currentTime;
            this.trackLength = trackLength;
            this.hitsoundMode = hitsoundMode;

            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
            Masking = true;
        }

        /// <summary>Time extent (ms) of a hit object marker, with its stable id and kind.</summary>
        private readonly record struct ObjBounds(int Id, double StartTime, double EndTime, HitObjectKind Kind);

        /// <summary>A lightweight description of one timing line or bookmark, realised into drawables on demand.</summary>
        private readonly record struct TimingEntry(double Time, bool IsBookmark, Color4 Colour, string Label, float PillY, int PointId, bool Coexists);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.82f },
                // Hitsound-lane band tints + separators (fixed; drawn behind the scrolling cells). Hidden
                // until the lanes editor is expanded.
                laneChrome = new Container { RelativeSizeAxes = Axes.X, Alpha = 0 },
                // The 1px white baseline that objects rest on (like a table top) and ticks hang below.
                new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Y = baseline_y,
                    Colour = Color4.White,
                },
                content = new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    // Width spans the whole song so the scrolling content's bounds always overlap the
                    // viewport - otherwise a zero-width container gets culled once it scrolls off-screen.
                    Width = contentWidth(),
                    Children = new Drawable[]
                    {
                        // Break regions (translucent grey bands) sit furthest back.
                        breakLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Timing-point lines (red = BPM, green = SV).
                        timingLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Objects sit behind the ticks so the beat grid stays readable over them.
                        objectLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Modding-mode discussion bubbles (above objects, below the grid ticks).
                        modLayer = new Container { RelativeSizeAxes = Axes.Both },
                        gridLayer = new Container { RelativeSizeAxes = Axes.Both },
                        selectionLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Review "Draw" stroke time-range bars (draggable to set when/how long a stroke shows).
                        annotationRangeLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Live slider-length preview (during placement / reshape), drawn on top.
                        previewLayer = new Container { RelativeSizeAxes = Axes.Both },
                    },
                },
                // The centre playhead the content scrolls beneath.
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2.5f,
                    Colour = OsuColour.Pink,
                },
                // Per-node hitsound cells: a manually-scrolled layer ABOVE the playhead so the cells sit over the
                // centre pink line (mirrors content.X each frame in updateLaneLayout). Hidden until expanded.
                laneCellsRoot = new Container { Alpha = 0 },
                // Left-edge lane labels (fixed, drawn on top of the scrolling cells). Hidden until expanded.
                laneLabels = new Container { RelativeSizeAxes = Axes.X, Alpha = 0 },
            };

            buildLaneChrome();

            buildObjects();

            // The gray "+ SV" hover pill rides on top of the scrolling content (above the grid + objects) so it
            // tracks the ticks. Hidden until the user Shift-hovers the timing-pill band.
            content.Add(svHoverPill = new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
                Y = baseline_y + 5,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 7f,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.62f, 0.62f, 0.66f, 1f) },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 6f, Vertical = 1f },
                        Text = "+ SV",
                        Colour = OsuColour.BackgroundDark,
                        Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                    },
                },
            });

            // Reflect selection changes from any source (here or the playfield).
            selection.Changed += refreshSelectionVisuals;
            nodeSelection.Changed += refreshSelectionVisuals;

            // Rebuild the timing/bookmark lines when the user customises their colours.
            settings.UninheritedColour.ValueChanged += _ => rebuildTimingPoints();
            settings.InheritedColour.ValueChanged += _ => rebuildTimingPoints();
            settings.BookmarkColour.ValueChanged += _ => rebuildTimingPoints();
            // Recolour the timeline objects live when the combo palette/toggle/map colours change.
            editable.ColoursChanged += buildObjects;
            // Toggling the hitsound editor adds/removes just the per-object lane cells (not a full rebuild - that
            // churned every drawable and made closing the panel hitch on GC after a long session).
            hitsoundMode.BindValueChanged(_ =>
            {
                // Opening the lanes editor eases the zoom in (via targetPixelsPerMs) if needed so cells never overlap.
                if (hitsoundMode.Value)
                    targetPixelsPerMs = Math.Max(targetPixelsPerMs, minPixelsPerMs());
                refreshLaneCells();
            });

            // A finer divisor raises the minimum zoom (cells sit closer); ease the zoom in if we'd now overlap.
            beatSnap.Value.BindValueChanged(_ =>
            {
                if (hitsoundMode.Value)
                    targetPixelsPerMs = Math.Max(targetPixelsPerMs, minPixelsPerMs());
            });
        }

        protected override void Update()
        {
            base.Update();

            // Ease the zoom toward its target (used by the hitsound min-zoom enforcement) rather than snapping.
            if (Math.Abs(pixelsPerMs - targetPixelsPerMs) > 1e-4f)
            {
                pixelsPerMs = (float)Interpolation.Lerp(targetPixelsPerMs, pixelsPerMs, Math.Exp(-0.018 * Time.Elapsed));
                if (Math.Abs(pixelsPerMs - targetPixelsPerMs) < 1e-3f)
                    pixelsPerMs = targetPixelsPerMs;
                buildObjects(); // object/cell positions depend on the zoom
            }

            // Scroll the content so the current time sits under the centre playhead.
            content.X = DrawWidth / 2f - (float)(currentTime() * pixelsPerMs);

            // Keep the content wide enough to always overlap the viewport. The initial width is derived
            // from the track length, but on a freshly created map the track length isn't known yet (it
            // reports 0), so the content would otherwise scroll off-screen and get masking-culled wholesale
            // - the timeline "vanishes" once the playhead passes its right edge. Grow it lazily here so the
            // grid/objects/timing layers stay realised regardless of when the track length becomes valid.
            float neededWidth = (float)(currentTime() * pixelsPerMs) + DrawWidth + 4000;
            if (content.Width < neededWidth)
                content.Width = neededWidth;

            updateGrid();
            updateLaneLayout();
            updateStrokeRangeBars();
            updateVisibleObjects();
            updateVisibleTimingPoints();
            updateSvHoverPill();

            // While a rubber-band box is held, keep extending the selection as the timeline scrolls under a
            // stationary cursor during playback (otherwise it only updates when the mouse actually moves).
            if (dragBox != null)
                updateDragSelection(lastDragScreenPos);
        }

        /// <summary>Total content width (px) spanning the whole song, plus a screen of padding.</summary>
        private float contentWidth()
        {
            double endMs = trackLength;
            if (beatmap.HitObjects.Count > 0)
            {
                var last = beatmap.HitObjects[^1];
                endMs = Math.Max(endMs, last.StartTime + last.Duration);
            }

            return (float)(endMs * pixelsPerMs) + 4000;
        }

        /// <summary>
        /// Sets the Review "Draw" stroke time-range bars: each is draggable (move = retime where it starts,
        /// right edge = how long it stays). Callbacks let the editor snapshot for undo + apply the new range.
        /// </summary>
        public void SetStrokeRanges(
            System.Collections.Generic.IReadOnlyList<(string Id, double Start, double End, Colour4 Colour)> ranges,
            Action onBegin, Action<string, double, double> onChanged, Action onCommit, Action<string> onDelete)
        {
            // Can be called before this timeline's own load runs (the editor refreshes annotations during its
            // BDL); the layer doesn't exist yet, and there are no ranges to show until Review mode anyway.
            if (annotationRangeLayer == null)
                return;

            annotationRangeLayer.Clear();
            strokeRangeBars.Clear();

            foreach (var r in ranges)
            {
                var bar = new StrokeRangeBar(r.Id, r.Start, r.End, r.Colour, () => pixelsPerMs)
                {
                    OnBegin = onBegin,
                    OnChanged = onChanged,
                    OnCommit = onCommit,
                    OnDelete = onDelete,
                };
                strokeRangeBars.Add(bar);
                annotationRangeLayer.Add(bar);
            }
        }

        private void updateStrokeRangeBars()
        {
            foreach (var bar in strokeRangeBars)
            {
                bar.X = (float)(bar.Start * pixelsPerMs);
                bar.Width = (float)Math.Max(4, (bar.End - bar.Start) * pixelsPerMs);
            }
        }

        /// <summary>
        /// A draggable bar on the timeline marking a Draw stroke's visible time range. Dragging the body retimes
        /// where it starts (keeping its length); dragging the right edge changes how long it stays. Position/width
        /// are driven by the parent each frame (<see cref="updateStrokeRangeBars"/>); this just edits Start/End.
        /// </summary>
        private partial class StrokeRangeBar : Container
        {
            public readonly string Id;
            public double Start;
            public double End;

            public Action? OnBegin;
            public Action<string, double, double>? OnChanged;
            public Action? OnCommit;
            public Action<string>? OnDelete;

            private readonly Colour4 colour;
            private readonly Func<float> pixelsPerMs;
            private bool resizing;
            private double grabTime, origStart, origEnd;

            public StrokeRangeBar(string id, double start, double end, Colour4 colour, Func<float> pixelsPerMs)
            {
                Id = id;
                Start = start;
                End = end;
                this.colour = colour;
                this.pixelsPerMs = pixelsPerMs;

                Height = 9;
                Anchor = Anchor.BottomLeft;
                Origin = Anchor.BottomLeft;
                Y = -3;
                Masking = true;
                CornerRadius = 3;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(colour.R, colour.G, colour.B, 0.5f) },
                    // Right-edge resize grip.
                    new Box
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = 5,
                        Colour = new Color4(colour.R, colour.G, colour.B, 0.95f),
                    },
                };
            }

            /// <summary>The map time under a screen position (the layer's local X maps to time via pixelsPerMs).</summary>
            private double timeAt(Vector2 screenSpacePos) => Parent!.ToLocalSpace(screenSpacePos).X / Math.Max(1e-4f, pixelsPerMs());

            // Right-click on the bar deletes the stroke (same as right-click on the stroke in the playfield).
            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button == osuTK.Input.MouseButton.Right)
                {
                    OnDelete?.Invoke(Id);
                    return true;
                }
                return base.OnMouseDown(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (e.Button != osuTK.Input.MouseButton.Left)
                    return false;

                OnBegin?.Invoke();
                origStart = Start;
                origEnd = End;
                grabTime = timeAt(e.ScreenSpaceMouseDownPosition);
                // Grab within the right grip = resize; otherwise move the whole range. Tiny bars are move-only.
                resizing = DrawWidth > 16 && ToLocalSpace(e.ScreenSpaceMouseDownPosition).X >= DrawWidth - 8;
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                double delta = timeAt(e.ScreenSpaceMousePosition) - grabTime;
                if (resizing)
                    End = Math.Max(origStart + 50, origEnd + delta);
                else
                {
                    Start = origStart + delta;
                    End = origEnd + delta;
                }
                OnChanged?.Invoke(Id, Start, End);
            }

            protected override void OnDragEnd(DragEndEvent e) => OnCommit?.Invoke();
        }

        /// <summary>Rebuilds the object markers (e.g. after a deletion changes the hit-object list).</summary>
        public void Rebuild() => buildObjects();

        /// <summary>
        /// Shows a live preview bar of a slider's time extent while it is being placed or reshaped, so the
        /// mapper sees in real time how long it will occupy. Cleared on commit (which rebuilds the objects).
        /// </summary>
        public void ShowSliderPreview(double startTime, double durationMs, double beatLength)
        {
            previewLayer.Clear();

            float x = (float)(startTime * pixelsPerMs);
            float w = (float)(durationMs * pixelsPerMs);

            previewLayer.Add(bar(x - object_size / 2f, w + object_size, OsuColour.Yellow));
            previewLayer.Add(dot(x, OsuColour.Yellow, null));
            previewLayer.Add(dot(x + w, OsuColour.Yellow, null));

            // A pill readout above the bar: the slider's length in beats at the active tempo.
            string label = beatLength > 0
                ? (durationMs / beatLength).ToString("0.##", CultureInfo.InvariantCulture) + " beats"
                : durationMs.ToString("0", CultureInfo.InvariantCulture) + " ms";

            previewLayer.Add(new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.BottomLeft,
                X = x,
                Y = object_centre_y - object_size / 2f - 2,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 7f,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Yellow },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 5f, Vertical = 1f },
                        Text = label,
                        Colour = OsuColour.BackgroundDark,
                        Font = FontUsage.Default.With(size: 12, weight: "Bold"),
                    },
                },
            });
        }

        public void ClearSliderPreview() => previewLayer.Clear();

        /// <summary>
        /// Lays out one marker container ("blueprint") per hit object at its time position. Each is kept
        /// in <see cref="blueprints"/> so a move drag can translate just the selected ones, lazer-style.
        /// </summary>
        private void buildObjects()
        {
            content.Width = contentWidth();
            objectLayer.Clear();
            previewLayer.Clear();
            objectBounds.Clear();
            objectIndexById.Clear();
            blueprints.Clear();
            blueprintBaseX.Clear();
            blueprintCircles.Clear();
            sliderNodeCircles.Clear();
            sliderBars.Clear();
            realizedIds.Clear();
            objectCells.Clear();
            for (int i = 0; i < 3; i++)
                laneCellContainers[i]?.Clear();

            buildTimingIndex();
            buildBreaks();
            buildMods();

            // Build the lightweight bounds for ALL objects (hit-testing needs the full set); the heavy drawables
            // are realised lazily for the visible window only.
            maxObjectDuration = 0;
            for (int i = 0; i < beatmap.HitObjects.Count; i++)
            {
                var o = beatmap.HitObjects[i];
                objectBounds.Add(new ObjBounds(o.Id, o.StartTime, o.StartTime + o.Duration, o.Kind));
                objectIndexById[o.Id] = i;
                if (o.Duration > maxObjectDuration)
                    maxObjectDuration = o.Duration;
            }

            updateVisibleObjects();
            updateVisibleTimingPoints();
            refreshSelectionVisuals();
        }

        /// <summary>The realised drawable for a single object (its timeline marker plus, when the hitsound editor
        /// is open, its lane cells). Built on demand by <see cref="updateVisibleObjects"/> as it scrolls into view.</summary>
        private void realizeObject(int index)
        {
            var o = beatmap.HitObjects[index];
            if (realizedIds.Contains(o.Id))
                return;

            float x = (float)(o.StartTime * pixelsPerMs);
            Colour4 comboColour = editable.ComboColourFor(o.ComboIndex);
            Color4 combo = new Color4(comboColour.R, comboColour.G, comboColour.B, comboColour.A);

            // Children are positioned relative to the blueprint (local 0 = the object's start).
            // An explicit width keeps the marker visible through the masked timeline as it scrolls.
            var blueprint = new Container
            {
                RelativeSizeAxes = Axes.Y,
                X = x,
                Width = (float)(o.Duration * pixelsPerMs) + HEIGHT,
            };

            var circles = new List<CircularContainer>();

            CircularContainer addDot(float dx, Color4 colour, int? comboNumber)
            {
                var c = dot(dx, colour, comboNumber);
                circles.Add(c);
                blueprint.Add(c);
                return c;
            }

            switch (o.Kind)
            {
                case HitObjectKind.Slider:
                    float w = (float)(o.Duration * pixelsPerMs);
                    // The body spans the head's left edge to the tail's right edge so it lines up with
                    // both circles, which then sit on top at the ends.
                    var bodyBar = bar(-object_size / 2f, w + object_size, combo);
                    blueprint.Add(bodyBar);
                    // bar() rests the body at 0.55 alpha; remember that so a body selection can tint it.
                    sliderBars[o.Id] = (bodyBar, new Color4(combo.R, combo.G, combo.B, 0.55f));
                    // Reverse indicator: a tick at each repeat boundary when the slider repeats (Slides >= 2).
                    for (int k = 1; k < o.Slides; k++)
                        blueprint.Add(repeatTick(w * k / o.Slides));
                    var tailDot = addDot(w, combo, null);            // tail (on the end tick)
                    var headDot = addDot(0, combo, o.ComboNumber);   // head, drawn on top
                    // A reverse triangle only on the slider's tail (end), pointing back toward the head.
                    if (o.Slides >= 2)
                        blueprint.Add(reverseArrow(w, -90));
                    // Head = node 0, tail = node Slides; these are individually selectable for hitsounding.
                    sliderNodeCircles[o.Id] = new Dictionary<int, CircularContainer>
                    {
                        [0] = headDot,
                        [Math.Max(1, o.Slides)] = tailDot,
                    };
                    break;

                case HitObjectKind.Spinner:
                    float sw = (float)(o.Duration * pixelsPerMs);
                    // A muted body spanning the spin, with end caps so the start/end are readable.
                    blueprint.Add(bar(-object_size / 2f, sw + object_size, OsuColour.TextMuted));
                    addDot(sw, OsuColour.TextMuted, null); // end
                    addDot(0, OsuColour.TextMuted, null);  // start, on top
                    break;

                default:
                    addDot(0, combo, o.ComboNumber);
                    break;
            }

            blueprintCircles[o.Id] = circles;
            objectLayer.Add(blueprint);
            blueprints[o.Id] = blueprint;
            blueprintBaseX[o.Id] = x;
            realizedIds.Add(o.Id);

            applyObjectSelectionVisual(o.Id);

            if (hitsoundMode.Value)
                realizeCells(o);
        }

        /// <summary>Removes a previously-realised object's drawables (marker + lane cells) once it scrolls away.</summary>
        private void derealizeObject(int id)
        {
            if (!realizedIds.Remove(id))
                return;

            if (blueprints.TryGetValue(id, out var bp))
                bp.Expire();

            blueprints.Remove(id);
            blueprintBaseX.Remove(id);
            blueprintCircles.Remove(id);
            sliderNodeCircles.Remove(id);
            sliderBars.Remove(id);

            if (objectCells.Remove(id, out var cells))
            {
                foreach (var c in cells)
                    c.Expire();
            }
        }

        /// <summary>
        /// Realises the object markers/cells whose time falls within the visible window (plus a screen of margin so
        /// they appear before scrolling in), and derealises those that have scrolled away. Runs every frame but is
        /// O(visible), not O(map size) - the key to staying smooth on dense maps.
        /// </summary>
        private void updateVisibleObjects()
        {
            if (objectBounds.Count == 0)
                return;

            double now = currentTime();
            double half = pixelsPerMs > 0 ? (DrawWidth / 2f) / pixelsPerMs : 0;
            double pad = pixelsPerMs > 0 ? (DrawWidth / 2f) / pixelsPerMs : 0; // half a screen of margin each side
            double from = now - half - pad;
            double to = now + half + pad;

            // Realise: objects whose [start,end] intersects the window. Start the scan at the first object that
            // could still be on screen (account for the longest slider/spinner reaching back into the window).
            // Each newly-realised object applies its own selection visual, so no O(map) refresh is needed here.
            int lo = lowerBoundByStart(from - maxObjectDuration);
            for (int i = lo; i < objectBounds.Count; i++)
            {
                var b = objectBounds[i];
                if (b.StartTime > to)
                    break;
                if (b.EndTime >= from && !realizedIds.Contains(b.Id))
                    realizeObject(i);
            }

            // Derealise: any realised object now fully outside the window.
            if (realizedIds.Count > 0)
            {
                _toDerealize.Clear();
                foreach (int id in realizedIds)
                {
                    if (objectIndexById.TryGetValue(id, out int idx))
                    {
                        var b = objectBounds[idx];
                        if (b.EndTime < from || b.StartTime > to)
                            _toDerealize.Add(id);
                    }
                }

                foreach (int id in _toDerealize)
                    derealizeObject(id);
            }
        }

        private readonly List<int> _toDerealize = new List<int>();

        /// <summary>Index of the first object whose StartTime is >= the given time (objectBounds is time-sorted).</summary>
        private int lowerBoundByStart(double time)
        {
            int lo = 0, hi = objectBounds.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (objectBounds[mid].StartTime < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>Builds and registers one object's hitsound lane cells (used while the hitsound editor is open).</summary>
        private void realizeCells(HitObjectModel o)
        {
            if (laneCellContainers[0] == null)
                return;

            var cells = new List<Drawable>(6);
            foreach (var (time, sample) in hitsoundColumns(o))
            {
                float x = (float)(time * pixelsPerMs);
                // Effective volume: the object's explicit override, else the timing point's volume at this column.
                float volume = o.SampleVolume > 0 ? o.SampleVolume : (TimingVolume?.Invoke(time) ?? 1f);
                // The lit cell shows the bank that actually drives this addition: the addition bank, or the
                // normal bank when the addition bank is Auto (osu! inherits it then). The hitnormal bank
                // itself lives in the bank bar, not on these whistle/finish/clap lanes.
                SampleBank additionBank = sample.AdditionBank != SampleBank.Auto ? sample.AdditionBank : sample.NormalBank;
                for (int lane = 0; lane < 3; lane++)
                {
                    var def = hitsoundLaneDefs[lane];
                    bool on = (sample.HitSound & def.Bit) != 0;
                    var cell = new LaneCell(x, time, def.Colour, on, bankLetter(additionBank), volume, hoverColumn);
                    laneCellContainers[lane].Add(cell);
                    cells.Add(cell);
                }
            }

            objectCells[o.Id] = cells;
        }

        /// <summary>
        /// Adds or removes just the per-object hitsound lane cells when the lanes editor is toggled, instead of the
        /// full <see cref="buildObjects"/> teardown/rebuild. That rebuild re-allocated every blueprint + break + mod
        /// + timing drawable on each toggle; over a long session the garbage occasionally tipped into a GC pause, so
        /// closing the panel would hitch. This touches only the cells (O(visible objects)). When opening with a zoom
        /// change, the zoom-ease's <see cref="buildObjects"/> still rebuilds cells too - harmless (it clears first).
        /// </summary>
        private void refreshLaneCells()
        {
            objectCells.Clear();
            for (int i = 0; i < 3; i++)
                laneCellContainers[i]?.Clear();

            if (!hitsoundMode.Value)
                return;

            foreach (int id in realizedIds)
                if (objectIndexById.TryGetValue(id, out int idx))
                    realizeCells(beatmap.HitObjects[idx]);
        }

        // --- Hitsound lanes ---

        /// <summary>Builds the static lane chrome: per-lane tint bands + separators (behind the cells), the three
        /// scrolling cell containers, and the fixed left control gutter. Called once on load; geometry is relative
        /// thirds so it follows the lane region as the timeline expands (see <see cref="updateLaneLayout"/>).</summary>
        private void buildLaneChrome()
        {
            // A faint full-height column highlight that follows the hovered cell (drawn first, behind the cells).
            laneCellsRoot.Add(laneColumnHighlight = new Box
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Y,
                Height = 1f,
                Width = cell_size + 6,
                Colour = EditorTheme.Colours.Text,
                Alpha = 0,
            });

            for (int i = 0; i < 3; i++)
            {
                var def = hitsoundLaneDefs[i];

                // A faint lane-coloured band with a 1px separator at its bottom edge.
                laneChrome.Add(new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Y,
                    Height = 1f / 3f,
                    Y = i / 3f,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = def.Colour, Alpha = 0.06f },
                        new Box
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = EditorTheme.Colours.Border,
                            Alpha = 0.6f,
                        },
                    },
                });

                // The scrolling cells for this lane.
                laneCellsRoot.Add(laneCellContainers[i] = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Y,
                    Height = 1f / 3f,
                    Y = i / 3f,
                });
            }

            // The slim fixed gutter: an opaque strip of C/W/F lane-colour tags on the far left, with a divider. The
            // hitsound controls themselves live in a separate left-side block (see EditorScreen), not here.
            laneLabels.Add(new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = lane_gutter,
                Masking = true,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                    buildLaneTagStrip(),
                    // Right-edge divider separating the gutter from the scrolling cells.
                    new Box
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = 1,
                        Colour = EditorTheme.Colours.Border,
                    },
                },
            });

            // Transient action-feedback effects (flash + ring), scrolling with the cells, drawn above them.
            laneCellsRoot.Add(laneEffects = new Container { RelativeSizeAxes = Axes.Both });
        }

        /// <summary>The slim far-left strip of the gutter: the three lane-colour tag letters (C / W / F), stacked.</summary>
        private Drawable buildLaneTagStrip()
        {
            var strip = new Container { RelativeSizeAxes = Axes.Y, Width = lane_tag_width };
            for (int i = 0; i < 3; i++)
            {
                var def = hitsoundLaneDefs[i];
                strip.Add(new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Y,
                    Height = 1f / 3f,
                    Y = i / 3f,
                    Children = new Drawable[]
                    {
                        // Lane-coloured accent down the left edge.
                        new Box { RelativeSizeAxes = Axes.Y, Width = 3, Colour = def.Colour },
                        new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = def.Label[..1], // C / W / F
                            Colour = def.Colour,
                            Font = FontUsage.Default.With(size: 13, weight: "Bold"),
                        },
                    },
                });
            }
            return strip;
        }

        /// <summary>Positions/sizes the three lane regions below the object band each frame, fading them in as the
        /// timeline expands. The region is everything below the fixed object band (<see cref="HEIGHT"/>px).</summary>
        private void updateLaneLayout()
        {
            float region = Math.Max(0, DrawHeight - HEIGHT);
            float alpha = Math.Clamp(region / 60f, 0, 1);

            laneChrome.Alpha = laneLabels.Alpha = laneCellsRoot.Alpha = alpha;
            laneChrome.Y = laneLabels.Y = laneCellsRoot.Y = HEIGHT;
            laneChrome.Height = laneLabels.Height = laneCellsRoot.Height = region;

            // laneCellsRoot lives outside `content` (so it draws above the playhead); mirror the scroll manually.
            laneCellsRoot.X = content.X;
            laneCellsRoot.Width = content.Width;
        }

        /// <summary>Enumerates the hitsound-bearing columns of an object: a circle/spinner is one column at its
        /// start; a slider is one column per node (head, each repeat, tail) using its per-node samples.</summary>
        private IEnumerable<(double Time, NodeSample Sample)> hitsoundColumns(HitObjectModel o)
        {
            if (o.Kind == HitObjectKind.Slider && o.Slides >= 1)
            {
                int nodes = o.Slides + 1;
                for (int k = 0; k < nodes; k++)
                {
                    double t = o.StartTime + o.Duration * k / o.Slides;
                    NodeSample ns = o.NodeSamples != null && k < o.NodeSamples.Count
                        ? o.NodeSamples[k]
                        : new NodeSample(o.HitSound, o.NormalBank, o.AdditionBank);
                    yield return (t, ns);
                }
            }
            else
            {
                yield return (o.StartTime, new NodeSample(o.HitSound, o.NormalBank, o.AdditionBank));
            }
        }

        private static char bankLetter(SampleBank bank) => bank switch
        {
            SampleBank.Normal => 'N',
            SampleBank.Soft => 'S',
            SampleBank.Drum => 'D',
            _ => 'A', // Auto
        };

        /// <summary>
        /// One hitsound lane cell: a filled lane-coloured chip with the addition-bank letter when the addition is
        /// on, or a faint hollow slot when off. A lit cell dims with its volume (0..1) so quiet notes read fainter.
        /// On hover it brightens and pops, and lights its whole time column (via the <c>onHoverColumn</c> callback)
        /// so node-by-node aiming is easier.
        /// </summary>
        private partial class LaneCell : Container
        {
            private readonly Box fill;
            private readonly bool on;
            private readonly float baseAlpha;
            private readonly double columnTime;
            private readonly Action<double, bool> onHoverColumn;

            public LaneCell(float x, double columnTime, Color4 colour, bool on, char letter, float volume, Action<double, bool> onHoverColumn)
            {
                this.on = on;
                this.columnTime = columnTime;
                this.onHoverColumn = onHoverColumn;
                baseAlpha = on ? 0.4f + 0.6f * Math.Clamp(volume, 0f, 1f) : 0.35f;

                Anchor = Anchor.CentreLeft;
                Origin = Anchor.Centre;
                X = x;
                Size = new Vector2(cell_size);
                Masking = true;
                CornerRadius = 6;
                BorderThickness = on ? 0 : 1.5f;
                BorderColour = on ? colour : EditorTheme.Colours.BorderStrong;
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = on ? colour : EditorTheme.Colours.Surface, Alpha = baseAlpha },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = on ? letter.ToString() : string.Empty,
                        Colour = OsuColour.BackgroundDark,
                        Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.12f, 90, Easing.OutQuint);
                fill.FadeTo(on ? 1f : 0.6f, 90, Easing.OutQuint);
                onHoverColumn(columnTime, true);
                return false; // visual only - don't block paint/toggle on the timeline
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                this.ScaleTo(1f, 150, Easing.OutQuint);
                fill.FadeTo(baseAlpha, 150, Easing.OutQuint);
                onHoverColumn(columnTime, false);
            }
        }

        /// <summary>
        /// Plays a brief action-feedback burst on a lane cell: a soft flash plus an expanding (or, when
        /// <paramref name="positive"/> is false, contracting) ring — the same feel as the playfield circle hit
        /// effects. Spawned on add / cycle / delete / paint so edits read instantly.
        /// </summary>
        private void spawnCellFeedback(double columnTime, int lane, Color4 colour, bool positive)
        {
            if (laneEffects == null)
                return;

            float x = (float)(columnTime * pixelsPerMs);

            var flash = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
            };

            // A rounded-square outline matching the cell shape (not a circle).
            var ring = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 2.5f,
                BorderColour = colour,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0, AlwaysPresent = true },
            };

            // Anchored top-left and offset by a fraction of the full lane region, so its centre lands exactly
            // on the cell (the effect layer spans all three lanes, not a single lane third).
            var fx = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Y,
                Position = new Vector2(x, (lane + 0.5f) / 3f),
                Size = new Vector2(cell_size),
                Children = new Drawable[] { ring, flash },
            };

            laneEffects.Add(fx);

            flash.FadeTo(positive ? 0.6f : 0.5f).FadeOut(240, Easing.OutQuint);
            ring.FadeTo(0.9f).FadeOut(300, Easing.OutQuint);
            ring.ScaleTo(1f).ScaleTo(positive ? 1.7f : 0.5f, 300, Easing.OutQuint);
            fx.Delay(320).Expire();
        }

        /// <summary>
        /// Rhythm feedback during playback: as the playhead crosses a node, the lane cells whose addition
        /// (whistle/finish/clap) actually sounds give a quick brightness pulse - no ring (that signals an edit).
        /// No-op unless the lanes editor is open. Called from the editor's hitsound playback loop.
        /// </summary>
        public void PulseHitsound(double columnTime, int hitSound)
        {
            if (laneEffects == null || !hitsoundMode.Value)
                return;

            // Every node gets a faint full-height column pulse, so you see the beat land even on a note with no
            // additions (which has no lit cell to flash). Lit whistle/finish/clap cells additionally pop white.
            pulseColumn(columnTime);

            for (int lane = 0; lane < 3; lane++)
            {
                if ((hitSound & hitsoundLaneDefs[lane].Bit) != 0)
                    pulseCell(columnTime, lane, hitsoundLaneDefs[lane].Colour);
            }
        }

        /// <summary>A faint full-height beat pulse over a column during playback (independent of the hover highlight).</summary>
        private void pulseColumn(double columnTime)
        {
            var col = new Box
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Y,
                Height = 1f,
                Width = cell_size + 6,
                X = (float)(columnTime * pixelsPerMs),
                Colour = EditorTheme.Colours.Text,
            };

            laneEffects.Add(col);
            col.FadeTo(0.13f).FadeOut(220, Easing.OutQuint);
            col.Delay(240).Expire();
        }

        /// <summary>
        /// A quick pop over one lane cell (the playback pulse). Flashes WHITE, not the lane colour - the cell is
        /// already lit in its lane colour during playback, so a same-colour flash would be invisible; white reads
        /// clearly. A small scale-out makes the beat pop. Lighter/shorter than an edit burst (no ring).
        /// </summary>
        private void pulseCell(double columnTime, int lane, Color4 colour)
        {
            float x = (float)(columnTime * pixelsPerMs);
            var flash = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Y,
                Position = new Vector2(x, (lane + 0.5f) / 3f),
                Size = new Vector2(cell_size),
                Masking = true,
                CornerRadius = 6,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
            };

            laneEffects.Add(flash);
            flash.FadeTo(0.85f).FadeOut(230, Easing.OutQuint);
            flash.ScaleTo(1f).ScaleTo(1.35f, 230, Easing.OutQuint);
            flash.Delay(250).Expire();
        }

        /// <summary>Shows/moves the faint full-height column highlight under a hovered lane cell (or hides it).</summary>
        private void hoverColumn(double columnTime, bool entered)
        {
            if (laneColumnHighlight == null)
                return;

            if (entered)
            {
                laneColumnHighlight.X = (float)(columnTime * pixelsPerMs);
                laneColumnHighlight.FadeTo(0.08f, 90, Easing.OutQuint);
            }
            else
            {
                laneColumnHighlight.FadeOut(150, Easing.OutQuint);
            }
        }

        // --- Hitsound-lane input (paint / toggle / bank cycle) ---

        /// <summary>Whether the lanes editor is open and the given local Y falls in the lane region; outputs the lane.</summary>
        private bool tryLaneAt(Vector2 screenPosition, out int lane)
        {
            lane = -1;
            float region = DrawHeight - HEIGHT;
            if (!hitsoundMode.Value || region < 12)
                return false;

            var local = ToLocalSpace(screenPosition);
            if (local.Y < HEIGHT || local.Y > DrawHeight)
                return false;

            // The left gutter belongs to its inline controls, not the cells - never paint/toggle there.
            if (local.X < lane_gutter)
                return false;

            lane = Math.Clamp((int)((local.Y - HEIGHT) / (region / 3f)), 0, 2);
            return true;
        }

        /// <summary>True when the position is over the lanes' control gutter (so its inline controls own the input).</summary>
        private bool inLaneGutter(Vector2 screenPosition)
        {
            if (!hitsoundMode.Value)
                return false;

            var local = ToLocalSpace(screenPosition);
            return local.Y > HEIGHT && local.Y <= DrawHeight && local.X < lane_gutter;
        }

        /// <summary>The node column nearest the given time (within a cell's reach), as an (objectId, nodeIndex) pair.</summary>
        private bool tryColumnAt(double time, out int objectId, out int nodeIndex, out double columnTime)
        {
            objectId = -1;
            nodeIndex = -1;
            columnTime = 0;
            double best = (cell_size / 2f + 2) / pixelsPerMs; // within about half a cell of a column

            foreach (var o in beatmap.HitObjects)
            {
                foreach (var (t, ni) in hitsoundColumnIndices(o))
                {
                    double d = Math.Abs(t - time);
                    if (d < best)
                    {
                        best = d;
                        objectId = o.Id;
                        nodeIndex = ni;
                        columnTime = t;
                    }
                }
            }

            return objectId >= 0;
        }

        /// <summary>The hitsound columns of an object as (time, nodeIndex): nodeIndex = -1 for a circle/spinner.</summary>
        private IEnumerable<(double Time, int NodeIndex)> hitsoundColumnIndices(HitObjectModel o)
        {
            if (o.Kind == HitObjectKind.Slider && o.Slides >= 1)
            {
                for (int k = 0; k <= o.Slides; k++)
                    yield return (o.StartTime + o.Duration * k / o.Slides, k);
            }
            else
            {
                yield return (o.StartTime, -1);
            }
        }

        /// <summary>Whether a given lane's addition is currently on for the (object, node) column.</summary>
        private bool cellOn(int objectId, int nodeIndex, int bit)
        {
            int idx = beatmap.HitObjects.FindIndex(o => o.Id == objectId);
            if (idx < 0)
                return false;

            var o = beatmap.HitObjects[idx];
            if (o.Kind == HitObjectKind.Slider && nodeIndex >= 0)
            {
                int hs = o.NodeSamples != null && nodeIndex < o.NodeSamples.Count ? o.NodeSamples[nodeIndex].HitSound : o.HitSound;
                return (hs & bit) != 0;
            }

            return (o.HitSound & bit) != 0;
        }

        /// <summary>The NORMAL sample bank currently in force for the (object, node) column (shown on the cells).</summary>
        private SampleBank cellBank(int objectId, int nodeIndex)
        {
            int idx = beatmap.HitObjects.FindIndex(o => o.Id == objectId);
            if (idx < 0)
                return SampleBank.Auto;

            var o = beatmap.HitObjects[idx];
            if (o.Kind == HitObjectKind.Slider && nodeIndex >= 0 && o.NodeSamples != null && nodeIndex < o.NodeSamples.Count)
                return o.NodeSamples[nodeIndex].NormalBank;

            return o.NormalBank;
        }

        /// <summary>Left-click on a cell: toggles that lane's addition off -> on -> off for the column under the cursor.</summary>
        private void toggleCellAt(double time, int lane)
        {
            if (!tryColumnAt(time, out int objectId, out int nodeIndex, out double columnTime))
                return;

            int bit = hitsoundLaneDefs[lane].Bit;
            bool on = cellOn(objectId, nodeIndex, bit);
            actions.SetHitsoundAddition(objectId, nodeIndex, bit, on: !on, pushUndoStep: true);
            spawnCellFeedback(columnTime, lane, on ? EditorTheme.Colours.Error : hitsoundLaneDefs[lane].Colour, positive: !on);
        }

        /// <summary>
        /// Shift+right-click on a cell: cycles the note's NORMAL bank Auto -> Normal -> Soft -> Drum -> Auto (the
        /// addition bank is set separately in the bank bar). If the cell is off, it's created first.
        /// </summary>
        private void cycleBankAt(double time, int lane)
        {
            if (!tryColumnAt(time, out int objectId, out int nodeIndex, out double columnTime))
                return;

            int bit = hitsoundLaneDefs[lane].Bit;
            if (!cellOn(objectId, nodeIndex, bit))
            {
                actions.SetHitsoundAddition(objectId, nodeIndex, bit, on: true, pushUndoStep: true);
                spawnCellFeedback(columnTime, lane, hitsoundLaneDefs[lane].Colour, positive: true);
                return;
            }

            SampleBank next = cellBank(objectId, nodeIndex) switch
            {
                SampleBank.Auto => SampleBank.Normal,
                SampleBank.Normal => SampleBank.Soft,
                SampleBank.Soft => SampleBank.Drum,
                _ => SampleBank.Auto, // Drum -> Auto
            };
            actions.SetHitsoundBank(objectId, nodeIndex, addition: false, next);
            spawnCellFeedback(columnTime, lane, hitsoundLaneDefs[lane].Colour, positive: true);
        }

        /// <summary>Applies the active paint stroke's on/off to the column under the cursor (once per column).</summary>
        private void paintCellAt(double time, int lane)
        {
            if (!tryColumnAt(time, out int objectId, out int nodeIndex, out double columnTime))
                return;

            var key = (objectId, nodeIndex);
            if (!paintedColumns.Add(key))
                return;

            int bit = hitsoundLaneDefs[lane].Bit;
            if (cellOn(objectId, nodeIndex, bit) == paintTurnOn)
                return; // already in the desired state - don't churn an undo step

            // The first cell that actually changes opens the undo step; the rest fold into it.
            bool first = !paintFirstApplied;
            paintFirstApplied = true;
            actions.SetHitsoundAddition(objectId, nodeIndex, bit, paintTurnOn, pushUndoStep: first);
            spawnCellFeedback(columnTime, lane,
                paintTurnOn ? hitsoundLaneDefs[lane].Colour : EditorTheme.Colours.Error, positive: paintTurnOn);
        }

        /// <summary>The editable timing-point id matching a derived marker (by time + red/green), or -1 if none.</summary>
        private int timingPointId(TimingMarker marker)
        {
            int t = marker.Time;
            foreach (var m in beatmap.TimingPointModels)
            {
                if ((int)Math.Round(m.Time) == t && m.Uninherited == marker.Uninherited)
                    return m.Id;
            }
            return -1;
        }

        /// <summary>
        /// Draws a translucent grey band over each break period (scrolling with the content), with a small
        /// "Break" label at its start - mirroring the kiai band but in neutral grey.
        /// </summary>
        private void buildBreaks()
        {
            breakLayer.Clear();

            var grey = EditorTheme.Colours.TextMuted;
            foreach (var br in beatmap.Breaks)
            {
                float x = (float)(br.Start * pixelsPerMs);
                float w = (float)((br.End - br.Start) * pixelsPerMs);

                breakLayer.Add(new Container
                {
                    X = x,
                    RelativeSizeAxes = Axes.Y,
                    Width = Math.Max(w, 2f),
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(grey.R, grey.G, grey.B, 0.16f) },
                        new SpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Margin = new MarginPadding { Left = 4, Top = 3 },
                            Text = "Break",
                            Colour = new Color4(grey.R, grey.G, grey.B, 0.9f),
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                });
            }
        }

        /// <summary>
        /// Sets the (already-filtered) discussions to draw as Modding-Mode bubbles on the timeline and rebuilds
        /// the bubble layer. Pass an empty list to clear them (e.g. when leaving Modding Mode).
        /// </summary>
        public void SetDiscussions(IReadOnlyList<Online.ModdingDiscussion> discussions)
        {
            modDiscussions = discussions ?? System.Array.Empty<Online.ModdingDiscussion>();
            buildMods();
        }

        /// <summary>Draws a small coloured bubble above the baseline at each timed discussion (Modding Mode).</summary>
        private void buildMods()
        {
            modLayer.Clear();

            foreach (var d in modDiscussions)
            {
                if (d.TimestampMs is not { } ms)
                    continue;

                float x = (float)(ms * pixelsPerMs);
                var colour = Online.ModdingDiscussion.TypeColour(d.MessageType);

                modLayer.Add(new ModBubble(d, colour)
                {
                    X = x,
                    Y = baseline_y - object_size - 6,
                    Clicked = () => ModBubbleClicked?.Invoke(ms),
                });
            }
        }

        /// <summary>
        /// Rebuilds the lightweight timing/bookmark index and re-realises the visible window. Called when the
        /// timing-line/bookmark colours change (the data itself is re-indexed by <see cref="buildObjects"/>).
        /// </summary>
        private void rebuildTimingPoints()
        {
            buildTimingIndex();
            updateVisibleTimingPoints();
        }

        /// <summary>
        /// Builds the time-sorted index of timing lines (red = uninherited/BPM, green = inherited/SV) and
        /// bookmarks. The heavy drawables are realised on demand by <see cref="updateVisibleTimingPoints"/>, so
        /// this stays O(timing points + bookmarks) once, not per frame - matching the object virtualisation.
        /// </summary>
        private void buildTimingIndex()
        {
            timingLayer.Clear();
            realizedTiming.Clear();
            timingEntries.Clear();

            // Bookmark times (rounded) that coincide with a timing point: there, the bookmark line is drawn
            // only above the baseline, leaving the lower part to the timing line + pill.
            var timingTimes = new HashSet<int>(beatmap.TimingPoints.Select(p => (int)Math.Round((double)p.Time)));

            // Group points that share a time so their pills can be stacked instead of overlapping: when a
            // red and a green sit on the same tick, one pill goes below the baseline and the other near the top.
            foreach (var group in beatmap.TimingPoints.GroupBy(p => p.Time))
            {
                // Render reds before greens so slot 0 (below the baseline) is the red and slot 1 (top) the green.
                var ordered = group.OrderByDescending(p => p.Uninherited).ToList();

                for (int slot = 0; slot < ordered.Count; slot++)
                {
                    var tp = ordered[slot];

                    Colour4 custom = tp.Uninherited ? settings.UninheritedColour.Value : settings.InheritedColour.Value;
                    Color4 colour = new Color4(custom.R, custom.G, custom.B, 0.85f);

                    // BPM for red, "x" multiplier for green.
                    string label = tp.Uninherited
                        ? tp.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : tp.Value.ToString("0.##", CultureInfo.InvariantCulture) + "x";

                    // Slot 0 sits just below the baseline; further slots stack up towards the top edge.
                    float pillY = slot == 0 ? baseline_y + 7 : 1f;

                    timingEntries.Add(new TimingEntry(tp.Time, false, colour, label, pillY, timingPointId(tp), false));
                }
            }

            Colour4 bm = settings.BookmarkColour.Value;
            Color4 bookmarkColour = new Color4(bm.R, bm.G, bm.B, 0.9f);

            foreach (int bookmark in beatmap.Bookmarks)
                timingEntries.Add(new TimingEntry(bookmark, true, bookmarkColour, string.Empty, 0, -1, timingTimes.Contains(bookmark)));

            timingEntries.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        /// <summary>
        /// Realises the timing lines/bookmarks whose time falls within the visible window (plus a screen of margin)
        /// and derealises those scrolled away. O(visible) per frame, not O(timing-point count).
        /// </summary>
        private void updateVisibleTimingPoints()
        {
            if (timingEntries.Count == 0)
                return;

            double now = currentTime();
            double margin = pixelsPerMs > 0 ? DrawWidth / pixelsPerMs : 0; // a full screen of padding each side
            double from = now - margin;
            double to = now + margin;

            for (int i = lowerBoundTiming(from); i < timingEntries.Count; i++)
            {
                if (timingEntries[i].Time > to)
                    break;
                if (!realizedTiming.ContainsKey(i))
                    realizeTimingEntry(i);
            }

            if (realizedTiming.Count > 0)
            {
                _toDerealizeTiming.Clear();
                foreach (var kv in realizedTiming)
                {
                    double t = timingEntries[kv.Key].Time;
                    if (t < from || t > to)
                        _toDerealizeTiming.Add(kv.Key);
                }

                foreach (int i in _toDerealizeTiming)
                    derealizeTimingEntry(i);
            }
        }

        /// <summary>Index of the first timing entry whose time is >= the given time (timingEntries is time-sorted).</summary>
        private int lowerBoundTiming(double time)
        {
            int lo = 0, hi = timingEntries.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (timingEntries[mid].Time < time)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return lo;
        }

        private void realizeTimingEntry(int index)
        {
            var e = timingEntries[index];
            float x = (float)(e.Time * pixelsPerMs);

            if (e.IsBookmark)
            {
                // A standalone bookmark spans the full height; one sharing a time with a timing point is drawn
                // only from the baseline upward, leaving the red/green line + pill visible below.
                var bookmark = new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopCentre,
                    Width = 2f,
                    X = x,
                    Y = 0,
                    Height = e.Coexists ? baseline_y : HEIGHT,
                    Colour = e.Colour,
                };
                timingLayer.Add(bookmark);
                realizedTiming[index] = new Drawable[] { bookmark };
                return;
            }

            var line = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
                // Confined to the object band so it doesn't extend down through the hitsound lanes.
                Height = HEIGHT,
                Width = 2f,
                X = x,
                Colour = e.Colour,
            };

            int pointId = e.PointId;
            var pill = new TimingPill(pointId, e.Label, new Color4(e.Colour.R, e.Colour.G, e.Colour.B, 1f))
            {
                BaseX = x + 3,
                BaseY = e.PillY,
                Clicked = pos => TimingPillClicked?.Invoke(pointId, pos),
            };

            timingLayer.Add(line);
            timingLayer.Add(pill);
            realizedTiming[index] = new Drawable[] { line, pill };
        }

        private void derealizeTimingEntry(int index)
        {
            if (!realizedTiming.TryGetValue(index, out var drawables))
                return;

            foreach (var d in drawables)
                d.Expire();

            realizedTiming.Remove(index);
        }

        /// <summary>
        /// Live preview of a time move: translates the selected blueprints (and the group box) by a time
        /// offset without rebuilding. The model is committed (and the offset reset) on drag release.
        /// </summary>
        public void PreviewTimeOffset(double offsetMs)
        {
            float dx = (float)(offsetMs * pixelsPerMs);

            foreach (int id in selection.Selected)
            {
                if (blueprints.TryGetValue(id, out var bp) && blueprintBaseX.TryGetValue(id, out float baseX))
                    bp.X = baseX + dx;
            }

            selectionLayer.X = dx;
        }

        /// <summary>
        /// A combo-coloured circle marker, like osu!lazer's timeline blips. Pass a combo number for the
        /// head (it renders inside), or null for a slider tail (no number).
        /// </summary>
        private CircularContainer dot(float x, Color4 colour, int? comboNumber)
        {
            var children = new List<Drawable> { new Box { RelativeSizeAxes = Axes.Both, Colour = colour } };

            if (comboNumber is int number)
            {
                children.Add(new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = number.ToString(),
                    Colour = Color4.White,
                    Font = FontUsage.Default.With(size: object_size * 0.62f, weight: "Bold"),
                });
            }

            // Anchored to the top so its centre sits at object_centre_y - i.e. resting on the baseline.
            return new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                X = x,
                Y = object_centre_y,
                Size = new Vector2(object_size),
                Masking = true,
                BorderThickness = 2,
                BorderColour = Color4.White,
                Children = children.ToArray(),
            };
        }

        /// <summary>A small reverse triangle drawn on a repeating slider's node (rotationDeg: -90 points back).</summary>
        private Drawable reverseArrow(float x, float rotationDeg) => new Triangle
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            X = x,
            Y = object_centre_y,
            Size = new Vector2(object_size * 0.52f),
            Rotation = rotationDeg,
            Colour = OsuColour.BackgroundDark,
        };

        /// <summary>A small white dot marking a slider repeat (reverse) boundary on the timeline bar.</summary>
        private Drawable repeatTick(float x) => new Circle
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            X = x,
            Y = object_centre_y,
            Size = new Vector2(object_size * 0.28f),
            Colour = Color4.White,
        };

        private Drawable bar(float x, float width, Color4 colour) => new Circle
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.CentreLeft,
            X = x,
            Y = object_centre_y,
            Height = object_size, // same thickness as the circles
            Width = Math.Max(width, object_size),
            Colour = new Color4(colour.R, colour.G, colour.B, 0.55f),
        };

        /// <summary>Reassigns pooled grid lines to the beat ticks currently visible in the viewport.</summary>
        private void updateGrid()
        {
            double now = currentTime();
            double halfWindowMs = (DrawWidth / 2f) / pixelsPerMs;
            double from = now - halfWindowMs;
            double to = now + halfWindowMs;

            // Emit the visible ticks straight into the pooled boxes. This runs every frame during playback,
            // so it avoids materialising an intermediate list/tuples (per-frame garbage that caused GC stutter).
            int used = emitGridTicks(from, to);

            for (int i = used; i < gridPool.Count; i++)
                gridPool[i].Alpha = 0;
        }

        private Box addGridLine()
        {
            // Ticks rise straight up from the baseline (behind the objects); height (px) varies by subtick.
            var box = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.BottomCentre,
                Y = baseline_y,
                RelativePositionAxes = Axes.None,
                Width = 1.5f,
            };
            gridLayer.Add(box);
            gridPool.Add(box);
            return box;
        }

        /// <summary>
        /// Assigns the beat-grid ticks visible within a time window directly to the pooled boxes, returning
        /// how many were used. Allocation-free per frame (no intermediate list/tuples), unlike a yield/list build.
        /// </summary>
        private int emitGridTicks(double from, double to)
        {
            if (beatmap.BeatPoints.Count == 0)
                return 0;

            const int safety_cap = 4000;
            int used = 0;

            for (int s = 0; s < beatmap.BeatPoints.Count; s++)
            {
                var point = beatmap.BeatPoints[s];
                double segEnd = s + 1 < beatmap.BeatPoints.Count ? beatmap.BeatPoints[s + 1].Time : Math.Max(to, trackLength);
                if (segEnd < from)
                    continue;
                if (point.Time > to)
                    break;

                double step = point.BeatLength / divisor;
                if (step <= 0)
                    continue;

                // Start at the first tick at/after the window's left edge within this segment.
                int k = Math.Max(0, (int)Math.Floor((from - point.Time) / step));
                double time = point.Time + k * step;

                while (time <= segEnd && time <= to && used < safety_cap)
                {
                    if (time >= from)
                    {
                        (Color4 colour, float height) = tickStyle(k, point.Meter);

                        Box line = used < gridPool.Count ? gridPool[used] : addGridLine();
                        used++;

                        line.Alpha = 1;
                        line.X = (float)(time * pixelsPerMs);
                        line.Colour = colour;
                        line.Height = height;
                    }

                    k++;
                    time = point.Time + k * step;
                }
            }

            return used;
        }

        /// <summary>
        /// Colour + height (px) for a tick by its musical position at the current snap divisor. Whole
        /// beats are white (measure downbeats taller); sub-beats are coloured by which snap level they
        /// fall on (1/2 red, 1/4 blue, 1/3 &amp; 1/6 purple, 1/8 yellow, ...) and share one height.
        /// </summary>
        private (Color4 colour, float height) tickStyle(int k, int meter)
        {
            int d = divisor;
            int withinBeat = ((k % d) + d) % d;

            if (withinBeat == 0)
            {
                // Long (measure) tick spaced so four whole beats fall between consecutive long ticks.
                int beatsPerBar = Math.Max(1, meter) + 1;
                bool measureStart = k % (d * beatsPerBar) == 0;
                return measureStart
                    ? (settings.MeasureLineColour.Value, settings.MeasureLineHeight.Value)
                    : (settings.BeatLineColour.Value, settings.BeatLineHeight.Value);
            }

            // The snap level is the denominator of (withinBeat / divisor) in lowest terms.
            int level = d / gcd(withinBeat, d);
            return (snapColour(level), settings.QuarterLineHeight.Value);
        }

        private Color4 snapColour(int level) => level switch
        {
            2 => settings.HalfBeatLineColour.Value,            // 1/2 - red (editable)
            4 => settings.QuarterBeatLineColour.Value,         // 1/4 - blue (editable)
            3 => new Color4(0.78f, 0.27f, 0.80f, 1f),          // 1/3 - magenta
            6 => new Color4(0.61f, 0.35f, 1f, 1f),             // 1/6 - purple
            8 => new Color4(1f, 0.82f, 0.25f, 1f),             // 1/8 - yellow
            _ => new Color4(0.6f, 0.6f, 0.65f, 1f),            // finer - grey
        };

        private static int gcd(int a, int b)
        {
            while (b != 0)
                (a, b) = (b, a % b);
            return a;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            // Only zoom while ALT is held; otherwise let the scroll bubble up to seek the track.
            if (!e.AltPressed)
                return false;

            float factor = e.ScrollDelta.Y > 0 ? 1.1f : 1 / 1.1f;
            // While the hitsound lanes are open, don't let the user zoom out past the point where adjacent
            // cells would overlap.
            float next = Math.Clamp(pixelsPerMs * factor, minPixelsPerMs(), max_px_per_ms);
            if (Math.Abs(next - pixelsPerMs) < 1e-5f)
                return true;

            // Scrolling is instant (responsive): snap both the value and its ease target.
            pixelsPerMs = targetPixelsPerMs = next;
            buildObjects(); // object positions/widths depend on the zoom
            return true;
        }

        /// <summary>
        /// The smallest allowed zoom (px per ms). Normally <see cref="min_px_per_ms"/>, but while the hitsound
        /// lanes editor is open it's raised so that one beat-snap division (the grid the cells sit on) is at
        /// least a cell wide — so cells never overlap at the current divisor, while still letting the user zoom
        /// out as the divisor gets coarser. Uses the fastest (smallest beat length) section to stay safe across
        /// tempo changes. Clamped to <see cref="max_px_per_ms"/>.
        /// </summary>
        private float minPixelsPerMs()
        {
            if (!hitsoundMode.Value || beatmap.BeatPoints.Count == 0)
                return min_px_per_ms;

            double minBeat = double.MaxValue;
            foreach (var bp in beatmap.BeatPoints)
                if (bp.BeatLength > 0 && bp.BeatLength < minBeat)
                    minBeat = bp.BeatLength;

            if (minBeat == double.MaxValue)
                return min_px_per_ms;

            // ms between adjacent snap ticks at the current divisor; one cell must fit in that span.
            double division = minBeat / divisor;
            float need = (float)(cell_size / division);
            return Math.Clamp(need, min_px_per_ms, max_px_per_ms);
        }

        // --- Selection (osu!lazer-style: click to select, CTRL to toggle, drag for a rubber-band box) ---

        /// <summary>The time (ms) under a screen-space position, in beatmap time.</summary>
        private double timeAt(Vector2 screenPosition) => (ToLocalSpace(screenPosition).X - content.X) / pixelsPerMs;

        /// <summary>Whether a screen position is inside the lower timing-pill band (below the baseline, above any lanes).</summary>
        private bool timingBandAt(Vector2 screenPosition)
        {
            float y = ToLocalSpace(screenPosition).Y;
            return y >= baseline_y && y <= HEIGHT;
        }

        /// <summary>The beat tick (at the current snap divisor) nearest the given time; false if there's no timing.</summary>
        private bool nearestTick(double time, out double tickTime)
        {
            tickTime = 0;
            if (beatmap.BeatPoints.Count == 0)
                return false;

            var point = beatmap.BeatPoints[0];
            foreach (var bp in beatmap.BeatPoints)
            {
                if (bp.Time <= time)
                    point = bp;
                else
                    break;
            }

            double step = point.BeatLength / divisor;
            if (step <= 0)
                return false;

            tickTime = point.Time + Math.Round((time - point.Time) / step) * step;
            return true;
        }

        /// <summary>
        /// Positions the gray "+ SV" pill below the tick nearest the cursor while Shift is held over the timing-pill
        /// band, so the user sees (and can click) where an SV point would land. Hidden otherwise.
        /// </summary>
        private void updateSvHoverPill()
        {
            var input = GetContainingInputManager();
            if (input == null || !input.CurrentState.Keyboard.ShiftPressed)
            {
                svHoverPill.Alpha = 0;
                return;
            }

            Vector2 mouse = input.CurrentState.Mouse.Position;
            Vector2 local = ToLocalSpace(mouse);
            bool inBand = local.X >= 0 && local.X <= DrawWidth && local.Y >= baseline_y && local.Y <= HEIGHT;

            if (!inBand || !nearestTick(timeAt(mouse), out double tickTime))
            {
                svHoverPill.Alpha = 0;
                return;
            }

            svHoverPill.X = (float)(tickTime * pixelsPerMs);
            svHoverPill.Alpha = 1;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // The lanes' control gutter is owned by its inline controls (banks/volume/index/copy-paste): don't claim
            // the press, so those child controls receive it (and the volume slider can drag).
            if (inLaneGutter(e.ScreenSpaceMousePosition))
                return false;

            // In the hitsound lanes, defer to click/drag/up handlers (left toggles, drag paints, Shift+right sets bank).
            if (tryLaneAt(e.ScreenSpaceMousePosition, out _))
            {
                hitsoundDragged = false;
                return true;
            }

            // Right-click quick-delete (M2): remove the object under the cursor, or the whole selection
            // if that object is part of it - matching osu!lazer's right-click delete.
            if (e.Button == MouseButton.Right)
            {
                int id = objectAt(timeAt(e.ScreenSpaceMousePosition), ToLocalSpace(e.ScreenSpaceMousePosition).Y);
                if (id >= 0)
                {
                    if (selection.Contains(id))
                        actions.DeleteSelected();
                    else
                        actions.DeleteObject(id);
                }

                return true;
            }

            // Receive the left press so click/drag route here.
            return true;
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Clicks on the control gutter belong to its inline controls; never clear the selection or paint there.
            if (inLaneGutter(e.ScreenSpaceMousePosition))
                return false;

            // Shift + click in the lower timing-pill band adds a green SV point at the nearest beat tick (the gray
            // "+ SV" pill previews exactly where it lands).
            if (e.ShiftPressed && timingBandAt(e.ScreenSpaceMousePosition)
                && nearestTick(timeAt(e.ScreenSpaceMousePosition), out double svTickTime))
            {
                actions.AddTimingPointAt(svTickTime, uninherited: false);
                return true;
            }

            // In the hitsound lanes: left-click toggles the hitsound on/off (the bank lives on Shift+right-click).
            if (tryLaneAt(e.ScreenSpaceMousePosition, out int lane))
            {
                toggleCellAt(timeAt(e.ScreenSpaceMousePosition), lane);
                return true;
            }

            double time = timeAt(e.ScreenSpaceMousePosition);
            float localY = ToLocalSpace(e.ScreenSpaceMousePosition).Y;
            int hit = objectAt(time, localY);

            if (hit < 0)
            {
                // Clicking empty space clears both selections (unless adding to the object selection).
                if (!Shortcut.CommandPressed(e))
                    selection.Clear();
                nodeSelection.Clear();
            }
            else if (Shortcut.CommandPressed(e))
            {
                selection.Toggle(hit);
                nodeSelection.Clear();
            }
            else
            {
                // Two-stage selection: the first click selects the whole object. Only once a slider is the sole
                // selection can an individual part be picked - head/tail (red node) or the body.
                bool alreadySole = selection.Selected.Count == 1 && selection.Contains(hit);

                if (!alreadySole)
                {
                    selection.SetSingle(hit);
                    nodeSelection.Clear();
                }
                else if (isSlider(hit))
                {
                    if (sliderNodeAt(hit, time, out int nodeIndex))
                        nodeSelection.Select(hit, nodeIndex);
                    else
                        nodeSelection.SelectBody(hit);
                }
            }

            // Selecting an object from the timeline switches the editor to the select tool (disarms placement).
            if (hit >= 0)
                ObjectSelectedHere?.Invoke();

            return true;
        }

        /// <summary>Whether the object with the given id is a slider.</summary>
        private bool isSlider(int id)
        {
            foreach (var b in objectBounds)
            {
                if (b.Id == id)
                    return b.Kind == HitObjectKind.Slider;
            }
            return false;
        }

        /// <summary>Whether a click at the given time lands on slider <paramref name="id"/>'s head or tail; returns the node index.</summary>
        private bool sliderNodeAt(int id, double time, out int nodeIndex)
        {
            nodeIndex = -1;
            foreach (var b in objectBounds)
            {
                if (b.Id != id || b.Kind != HitObjectKind.Slider)
                    continue;

                double tol = (object_size / 2f + 3f) / pixelsPerMs;
                bool nearHead = Math.Abs(time - b.StartTime) <= tol;
                bool nearTail = Math.Abs(time - b.EndTime) <= tol;

                if (nearTail && (!nearHead || Math.Abs(time - b.EndTime) < Math.Abs(time - b.StartTime)))
                {
                    nodeIndex = sliderNodeCircles.TryGetValue(id, out var nodes) ? nodes.Keys.Max() : 1;
                    return true;
                }

                if (nearHead)
                {
                    nodeIndex = 0;
                    return true;
                }

                return false;
            }

            return false;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            // A drag starting on the control gutter belongs to its inline controls (e.g. the volume slider), not a
            // paint stroke or rubber-band select.
            if (inLaneGutter(e.ScreenSpaceMouseDownPosition))
                return false;

            // A drag in the hitsound lanes paints a whole row: left adds the addition, right erases it.
            if ((e.Button == MouseButton.Left || e.Button == MouseButton.Right) && tryLaneAt(e.ScreenSpaceMouseDownPosition, out int paintLane))
            {
                hitsoundPainting = true;
                hitsoundDragged = true;
                paintLaneIndex = paintLane;
                paintTurnOn = e.Button == MouseButton.Left;
                paintFirstApplied = false;
                paintedColumns.Clear();
                paintCellAt(timeAt(e.ScreenSpaceMouseDownPosition), paintLane);
                return true;
            }

            double startTime = timeAt(e.ScreenSpaceMouseDownPosition);
            int id = objectAt(startTime, ToLocalSpace(e.ScreenSpaceMouseDownPosition).Y);

            // Grabbing a slider's tail drags its reverse (repeat) count instead of moving it, like lazer - but with
            // Shift held it instead changes the slider's velocity (its duration), so Shift+drag never adds reverses.
            if (e.Button == MouseButton.Left && id >= 0 && isSliderTailGrab(id, startTime))
            {
                if (!selection.Contains(id))
                    selection.SetSingle(id);

                if (e.ShiftPressed)
                {
                    velocityDragging = true;
                    actions.BeginSliderVelocityDrag(id);
                }
                else
                {
                    repeatDragging = true;
                    actions.BeginSliderRepeatDrag(id);
                }
                return true;
            }

            // Grabbing a spinner's tail drags its end time, changing its duration.
            if (e.Button == MouseButton.Left && id >= 0 && isSpinnerTailGrab(id, startTime))
            {
                if (!selection.Contains(id))
                    selection.SetSingle(id);

                spinnerResizing = true;
                actions.BeginSpinnerDurationDrag(id);
                return true;
            }

            // Dragging an object moves the selection in time; dragging empty space rubber-band selects.
            if (e.Button == MouseButton.Left && id >= 0)
            {
                if (!selection.Contains(id))
                    selection.SetSingle(id);

                moving = true;
                moveGrabbedId = id;
                moveStartTime = startTime;
                actions.BeginMove();
                return true;
            }

            dragStartTime = startTime;
            dragBaseline = Shortcut.CommandPressed(e) ? new HashSet<int>(selection.Selected) : new HashSet<int>();

            content.Add(dragBox = new Box
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.Y,
                Height = 0.92f,
                Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.2f),
            });

            lastDragScreenPos = e.ScreenSpaceMousePosition;
            updateDragSelection(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (hitsoundPainting)
                paintCellAt(timeAt(e.ScreenSpaceMousePosition), paintLaneIndex);
            else if (moving)
                actions.MoveSelectionTime(timeAt(e.ScreenSpaceMousePosition) - moveStartTime, moveGrabbedId);
            else if (velocityDragging)
                actions.DragSliderVelocityTo(timeAt(e.ScreenSpaceMousePosition));
            else if (repeatDragging)
                actions.DragSliderRepeatTo(timeAt(e.ScreenSpaceMousePosition));
            else if (spinnerResizing)
                actions.DragSpinnerEndTo(timeAt(e.ScreenSpaceMousePosition));
            else
            {
                lastDragScreenPos = e.ScreenSpaceMousePosition;
                updateDragSelection(e.ScreenSpaceMousePosition);
            }
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (hitsoundPainting)
            {
                hitsoundPainting = false;
                paintedColumns.Clear();
                return;
            }

            if (moving)
            {
                moving = false;
                actions.EndMove();
                return;
            }

            if (velocityDragging)
            {
                velocityDragging = false;
                actions.EndSliderVelocityDrag();
                return;
            }

            if (repeatDragging)
            {
                repeatDragging = false;
                actions.EndSliderRepeatDrag();
                return;
            }

            if (spinnerResizing)
            {
                spinnerResizing = false;
                actions.EndSpinnerDurationDrag();
                return;
            }

            // A rubber-band drag that ended up selecting at least one object switches to the select tool too -
            // same as a single click does (OnMouseDown), which the box-select path otherwise missed.
            if (dragBox != null && selection.Selected.Count > 0)
                ObjectSelectedHere?.Invoke();

            dragBox?.Expire();
            dragBox = null;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            // Shift+right-click (no drag) in the hitsound lanes cycles that cell's sample bank (N/S/D). A right-DRAG
            // erases (handled by the paint stroke via hitsoundDragged), so it isn't double-processed here.
            if (e.Button == MouseButton.Right && e.ShiftPressed && !hitsoundDragged && tryLaneAt(e.ScreenSpaceMousePosition, out int lane))
                cycleBankAt(timeAt(e.ScreenSpaceMousePosition), lane);

            base.OnMouseUp(e);
        }

        private void updateDragSelection(Vector2 screenPosition)
        {
            double now = timeAt(screenPosition);
            double from = Math.Min(dragStartTime, now);
            double to = Math.Max(dragStartTime, now);

            if (dragBox != null)
            {
                dragBox.X = (float)(from * pixelsPerMs);
                dragBox.Width = (float)((to - from) * pixelsPerMs);
            }

            // Selection = the pre-drag baseline plus every object whose time extent overlaps the box.
            var inBox = objectBounds.Where(b => b.StartTime <= to && b.EndTime >= from).Select(b => b.Id);
            selection.SetRange(dragBaseline.Concat(inBox));
        }

        /// <summary>
        /// Whether a press at the given time lands on a slider's tail (so the drag should change its reverse
        /// count rather than move it): the object is a slider and the press is nearer its end than its start.
        /// </summary>
        private bool isSliderTailGrab(int id, double time)
        {
            foreach (var b in objectBounds)
            {
                if (b.Id != id)
                    continue;

                if (b.Kind != HitObjectKind.Slider || b.EndTime <= b.StartTime)
                    return false;

                double tol = (HEIGHT * 0.52f / 2f) / pixelsPerMs;
                return Math.Abs(time - b.EndTime) <= tol && Math.Abs(time - b.EndTime) < Math.Abs(time - b.StartTime);
            }

            return false;
        }

        /// <summary>Whether a press at the given time lands on a spinner's tail (so the drag resizes it).</summary>
        private bool isSpinnerTailGrab(int id, double time)
        {
            foreach (var b in objectBounds)
            {
                if (b.Id != id)
                    continue;

                if (b.Kind != HitObjectKind.Spinner || b.EndTime <= b.StartTime)
                    return false;

                double tol = (HEIGHT * 0.52f / 2f) / pixelsPerMs;
                return Math.Abs(time - b.EndTime) <= tol && Math.Abs(time - b.EndTime) < Math.Abs(time - b.StartTime);
            }

            return false;
        }

        /// <summary>
        /// The id of the object whose marker the given (time, localY) press falls on, or -1 if none. The marker
        /// is treated as a capsule - a circular head and tail of <see cref="object_size"/> with the body (for
        /// sliders/spinners) between them - so the round circles have round hit areas (not square bounding
        /// boxes), and pressing above/below or in a corner falls through to a rubber-band selection.
        /// </summary>
        private int objectAt(double time, float localY)
        {
            float dy = localY - object_centre_y;
            float radius = object_size / 2f + 3f;

            int best = -1;
            float bestDistance = float.MaxValue;

            foreach (var b in objectBounds)
            {
                // Work in pixels relative to the object's head: x runs from 0 (head) to durationPx (tail).
                float dx = (float)((time - b.StartTime) * pixelsPerMs);
                float durationPx = (float)((b.EndTime - b.StartTime) * pixelsPerMs);

                // Distance from the press to the head→tail segment (a point for zero-duration circles).
                float clampedX = Math.Clamp(dx, 0f, durationPx);
                float distance = MathF.Sqrt((dx - clampedX) * (dx - clampedX) + dy * dy);

                if (distance <= radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = b.Id;
                }
            }

            return best;
        }

        /// <summary>
        /// Marks the selection by giving the selected objects' circle markers a glowing yellow border, plus a
        /// single glowing yellow line running parallel to the baseline across the selection's time extent.
        /// </summary>
        /// <summary>Applies the selection visual (circle borders, red node, yellow body tint) to one realised object.</summary>
        private void applyObjectSelectionVisual(int id)
        {
            if (blueprintCircles.TryGetValue(id, out var circles))
            {
                bool sel = selection.Contains(id);
                foreach (var c in circles)
                {
                    c.BorderColour = sel ? OsuColour.Yellow : Color4.White;
                    c.BorderThickness = sel ? 3f : 2f;
                    c.EdgeEffect = sel ? yellowGlow(0.55f, 5f) : default;
                }
            }

            // A selected slider node (for hitsounding) overrides its circle to glowing red.
            if (nodeSelection.Selected is { } node && node.ObjectId == id
                && sliderNodeCircles.TryGetValue(id, out var nodes)
                && nodes.TryGetValue(node.NodeIndex, out var nodeCircle))
            {
                nodeCircle.BorderColour = EditorTheme.Colours.Error;
                nodeCircle.BorderThickness = 3.5f;
                nodeCircle.EdgeEffect = redGlow(0.6f, 6f);
            }

            // A selected slider body tints its bar yellow; otherwise it rests at its combo colour.
            if (sliderBars.TryGetValue(id, out var entry))
            {
                bool bodySelected = nodeSelection.IsBodySelected(id);
                entry.Bar.Colour = bodySelected
                    ? new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.8f)
                    : entry.RestColour;
            }
        }

        private void refreshSelectionVisuals()
        {
            selectionLayer.Clear();
            selectionLayer.X = 0; // reset any live move-preview offset

            // Apply each realised object's selection visual (borders / node / body tint).
            foreach (var id in realizedIds)
                applyObjectSelectionVisual(id);

            double minStart = double.MaxValue;
            double maxEnd = double.MinValue;

            foreach (var b in objectBounds)
            {
                if (!selection.Contains(b.Id))
                    continue;

                minStart = Math.Min(minStart, b.StartTime);
                maxEnd = Math.Max(maxEnd, b.EndTime);
            }

            if (minStart > maxEnd)
                return; // nothing selected

            float pad = object_size * 0.5f;
            float startX = (float)(minStart * pixelsPerMs);
            float endX = (float)(maxEnd * pixelsPerMs);

            // A thin, subtly-glowing yellow line just below the baseline, spanning the selection's time extent.
            selectionLayer.Add(new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                X = startX - pad,
                Y = baseline_y + 4f,
                Width = (endX - startX) + pad * 2,
                Height = 1.5f,
                Masking = true,
                CornerRadius = 0.75f,
                EdgeEffect = yellowGlow(0.4f, 3f),
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Yellow },
            });
        }

        private static EdgeEffectParameters yellowGlow(float alpha, float radius) => new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, alpha),
            Radius = radius,
        };

        private static EdgeEffectParameters redGlow(float alpha, float radius)
        {
            var red = EditorTheme.Colours.Error;
            return new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(red.R, red.G, red.B, alpha),
                Radius = radius,
            };
        }

        /// <summary>
        /// A timing-point readout pill on the timeline. Hovering scales it up about its own centre (so it grows
        /// in place without shifting); clicking restores its size and raises <see cref="Clicked"/> with its
        /// bottom screen position, so the editor can pop up an inline value editor beneath it.
        /// </summary>
        private partial class TimingPill : CircularContainer
        {
            /// <summary>The left/top the pill anchors to (its centre is offset from here once its size is known).</summary>
            public float BaseX { private get; set; }
            public float BaseY { private get; set; }

            public Action<Vector2>? Clicked;

            private readonly int id;
            private readonly string label;
            private readonly Color4 colour;

            public TimingPill(int id, string label, Color4 colour)
            {
                this.id = id;
                this.label = label;
                this.colour = colour;

                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre; // scale about the centre so hover-grow doesn't move it
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 5.5f;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                    new SpriteText
                    {
                        Padding = new MarginPadding { Horizontal = 4f, Vertical = 0.5f },
                        Text = label,
                        Colour = OsuColour.BackgroundDark,
                        Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                    },
                };
            }

            protected override void Update()
            {
                base.Update();
                // Position by centre so the pill's left edge stays at BaseX and its row at BaseY, regardless of
                // its auto-sized width (which depends on the label text).
                Position = new Vector2(BaseX + DrawWidth / 2f, BaseY + DrawHeight / 2f);
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.18f, 120, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.ScaleTo(1f, 120, Easing.OutQuint);

            protected override bool OnClick(ClickEvent e)
            {
                this.ScaleTo(1f, 80, Easing.OutQuint);
                Clicked?.Invoke(ScreenSpaceDrawQuad.BottomLeft);
                return true;
            }
        }

        /// <summary>
        /// A Modding-Mode discussion marker on the timeline: a small coloured circle (type colour) carrying the
        /// modder's initial, with a thin stalk down to the baseline and the mod text as a tooltip. Click seeks.
        /// </summary>
        private partial class ModBubble : CircularContainer, osu.Framework.Graphics.Cursor.IHasTooltip
        {
            public Action? Clicked;

            public osu.Framework.Localisation.LocalisableString TooltipText { get; }

            private readonly Color4 colour;
            private readonly string initial;

            public ModBubble(Online.ModdingDiscussion d, Color4 colour)
            {
                this.colour = colour;
                initial = string.IsNullOrEmpty(d.Username) ? "?" : d.Username.Substring(0, 1).ToUpperInvariant();

                string label = $"{d.Username} - {Online.ModdingDiscussion.TypeLabel(d.MessageType)}";
                if (d.Resolved)
                    label += " [resolved]";
                TooltipText = $"{label}\n{d.Message}";

                Anchor = Anchor.TopLeft;
                Origin = Anchor.Centre;
                Size = new Vector2(16);
                Masking = true;
                BorderThickness = 2;
                BorderColour = colour;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AddRangeInternal(new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = initial,
                        Colour = colour,
                        Font = FontUsage.Default.With(size: 10, weight: "Bold"),
                    },
                });
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.25f, 120, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.ScaleTo(1f, 120, Easing.OutQuint);

            protected override bool OnClick(ClickEvent e)
            {
                Clicked?.Invoke();
                return true;
            }
        }
    }
}
