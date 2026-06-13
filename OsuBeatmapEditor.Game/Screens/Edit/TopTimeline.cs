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

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private EditableBeatmap editable { get; set; } = null!;

        [Resolved]
        private BeatSnapDivisor beatSnap { get; set; } = null!;

        /// <summary>The active beat-snap resolution (ticks per beat).</summary>
        private int divisor => beatSnap.Value.Value;

        private float pixelsPerMs = 0.4f;

        // Move-drag state (dragging an object shifts the selection in time).
        private bool moving;
        private int moveGrabbedId;
        private double moveStartTime;

        // Slider repeat-drag state (dragging a slider's tail changes its reverse count, like osu!lazer).
        private bool repeatDragging;

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
        private Container selectionLayer = null!;
        private Container previewLayer = null!;
        private readonly List<Box> gridPool = new List<Box>();

        // --- Hitsound lanes (the expanded Clap/Whistle/Finish editor) ---
        private const int hs_whistle = 0b0010, hs_finish = 0b0100, hs_clap = 0b1000;

        /// <summary>The three addition lanes, top to bottom, with their hitSound bit and accent colour.</summary>
        private static readonly (string Label, int Bit, Color4 Colour)[] hitsoundLaneDefs =
        {
            ("CLAP", hs_clap, EditorTheme.Colours.Velocity),    // green
            ("WHISTLE", hs_whistle, EditorTheme.Colours.Bookmark), // blue
            ("FINISH", hs_finish, EditorTheme.Colours.Kiai),    // orange
        };

        // Hitsound-lane cell visuals + the fixed opaque label gutter on the left of the lanes.
        private const float cell_size = 28f;
        private const float lane_gutter = 96f;

        // Band tints/separators (fixed, drawn behind the cells); the scrolling cells; the left label gutter (on top).
        private Container laneChrome = null!;
        private Container laneCellsRoot = null!;
        private Container laneLabels = null!;
        private Container laneEffects = null!;
        private readonly Container[] laneCellContainers = new Container[3];

        // Selection state, mirroring lazer: a shared selection + a live drag box.
        private readonly List<ObjBounds> objectBounds = new List<ObjBounds>();
        private readonly Dictionary<int, Container> blueprints = new Dictionary<int, Container>();
        private readonly Dictionary<int, float> blueprintBaseX = new Dictionary<int, float>();

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
                        gridLayer = new Container { RelativeSizeAxes = Axes.Both },
                        selectionLayer = new Container { RelativeSizeAxes = Axes.Both },
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

            // Reflect selection changes from any source (here or the playfield).
            selection.Changed += refreshSelectionVisuals;
            nodeSelection.Changed += refreshSelectionVisuals;

            // Rebuild the timing/bookmark lines when the user customises their colours.
            settings.UninheritedColour.ValueChanged += _ => buildTimingPoints();
            settings.InheritedColour.ValueChanged += _ => buildTimingPoints();
            settings.BookmarkColour.ValueChanged += _ => buildTimingPoints();
            // Recolour the timeline objects live when the combo palette/toggle/map colours change.
            editable.ColoursChanged += buildObjects;
            // Toggling the hitsound editor adds/removes the per-object lane cells: rebuild the visible set.
            hitsoundMode.BindValueChanged(_ => buildObjects());
        }

        protected override void Update()
        {
            base.Update();

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
            updateVisibleObjects();

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

            buildTimingPoints();
            buildBreaks();

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
                for (int lane = 0; lane < 3; lane++)
                {
                    var def = hitsoundLaneDefs[lane];
                    bool on = (sample.HitSound & def.Bit) != 0;
                    // The cell's letter shows the note's NORMAL bank; the addition bank lives in the bank bar.
                    var cell = makeCell(x, def.Colour, on, sample.NormalBank);
                    laneCellContainers[lane].Add(cell);
                    cells.Add(cell);
                }
            }

            objectCells[o.Id] = cells;
        }

        // --- Hitsound lanes ---

        /// <summary>Builds the static lane chrome: per-lane tint bands + separators (behind the cells), the three
        /// scrolling cell containers, and the left-edge labels. Called once on load; geometry is relative thirds
        /// so it follows the lane region as the timeline expands (see <see cref="updateLaneLayout"/>).</summary>
        private void buildLaneChrome()
        {
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

                // A fixed-width OPAQUE gutter column on the left holds the lane label (room for more controls later).
                laneLabels.Add(new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Y,
                    Height = 1f / 3f,
                    Y = i / 3f,
                    Child = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        RelativeSizeAxes = Axes.Y,
                        Width = lane_gutter,
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                            // A thin lane-coloured accent down the left edge of the gutter.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = 3,
                                Colour = def.Colour,
                            },
                            // Right-edge divider separating the gutter from the cells.
                            new Box
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                RelativeSizeAxes = Axes.Y,
                                Width = 1,
                                Colour = EditorTheme.Colours.Border,
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Left = 14 },
                                Text = def.Label,
                                Colour = def.Colour,
                                Font = FontUsage.Default.With(size: 13, weight: "Bold"),
                            },
                        },
                    },
                });
            }

            // Transient action-feedback effects (flash + ring), scrolling with the cells, drawn above them.
            laneCellsRoot.Add(laneEffects = new Container { RelativeSizeAxes = Axes.Both });
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

        /// <summary>One hitsound cell: a filled lane-coloured chip with the addition-bank letter when the addition
        /// is on, or a faint hollow slot when off.</summary>
        private Drawable makeCell(float x, Color4 colour, bool on, SampleBank bank) => new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.Centre,
            X = x,
            Size = new Vector2(cell_size),
            Masking = true,
            CornerRadius = 6,
            BorderThickness = on ? 0 : 1.5f,
            BorderColour = on ? colour : EditorTheme.Colours.BorderStrong,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = on ? colour : EditorTheme.Colours.Surface,
                    Alpha = on ? 1f : 0.35f,
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = on ? bankLetter(bank).ToString() : string.Empty,
                    Colour = OsuColour.BackgroundDark,
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            },
        };

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

            var ring = new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                BorderThickness = 2.5f,
                BorderColour = colour,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0, AlwaysPresent = true },
            };

            var fx = new Container
            {
                Anchor = Anchor.CentreLeft,
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

        // --- Hitsound-lane input (paint / toggle / bank cycle) ---

        /// <summary>Whether the lanes editor is open and the given local Y falls in the lane region; outputs the lane.</summary>
        private bool tryLaneAt(Vector2 screenPosition, out int lane)
        {
            lane = -1;
            float region = DrawHeight - HEIGHT;
            if (!hitsoundMode.Value || region < 12)
                return false;

            float y = ToLocalSpace(screenPosition).Y;
            if (y < HEIGHT || y > DrawHeight)
                return false;

            lane = Math.Clamp((int)((y - HEIGHT) / (region / 3f)), 0, 2);
            return true;
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

        /// <summary>Left-click on a cell: creates the hitsound (turns the addition on). No-op if already on.</summary>
        private void createCellAt(double time, int lane)
        {
            if (!tryColumnAt(time, out int objectId, out int nodeIndex, out double columnTime))
                return;

            int bit = hitsoundLaneDefs[lane].Bit;
            if (!cellOn(objectId, nodeIndex, bit))
            {
                actions.SetHitsoundAddition(objectId, nodeIndex, bit, on: true, pushUndoStep: true);
                spawnCellFeedback(columnTime, lane, hitsoundLaneDefs[lane].Colour, positive: true);
            }
        }

        /// <summary>
        /// Shift+left-click on a cell: cycles the note's NORMAL bank Auto -> Normal -> Soft -> Drum -> Auto (the
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

        /// <summary>Right-click on a cell: deletes (clears) that lane's addition for the column under the cursor.</summary>
        private void deleteCellAt(double time, int lane)
        {
            if (!tryColumnAt(time, out int objectId, out int nodeIndex, out double columnTime))
                return;

            int bit = hitsoundLaneDefs[lane].Bit;
            if (cellOn(objectId, nodeIndex, bit))
            {
                actions.SetHitsoundAddition(objectId, nodeIndex, bit, on: false, pushUndoStep: true);
                spawnCellFeedback(columnTime, lane, EditorTheme.Colours.Error, positive: false);
            }
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
        /// Draws a vertical line at each timing point, scrolling with the content: red for uninherited
        /// (BPM) points, green for inherited (slider-velocity) ones - matching osu!lazer's timeline.
        /// </summary>
        private void buildTimingPoints()
        {
            timingLayer.Clear();

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

                    float x = (float)(tp.Time * pixelsPerMs);

                    timingLayer.Add(new Box
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopCentre,
                        // Confined to the object band so it doesn't extend down through the hitsound lanes.
                        Height = HEIGHT,
                        Width = 2f,
                        X = x,
                        Colour = colour,
                    });

                    // A pill readout just right of the line: BPM for red, "x" multiplier for green.
                    string label = tp.Uninherited
                        ? tp.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : tp.Value.ToString("0.##", CultureInfo.InvariantCulture) + "x";

                    // Slot 0 sits just below the baseline; further slots stack up towards the top edge.
                    float pillY = slot == 0 ? baseline_y + 7 : 1f;

                    int pointId = timingPointId(tp);
                    timingLayer.Add(new TimingPill(pointId, label, new Color4(colour.R, colour.G, colour.B, 1f))
                    {
                        BaseX = x + 3,
                        BaseY = pillY,
                        Clicked = pos => TimingPillClicked?.Invoke(pointId, pos),
                    });
                }
            }

            // Bookmarks: a vertical line in the user's bookmark colour. A standalone bookmark spans the full
            // height; one sharing a time with a timing point is drawn only from the baseline upward, leaving
            // the red/green line + pill visible below.
            Colour4 bm = settings.BookmarkColour.Value;
            Color4 bookmarkColour = new Color4(bm.R, bm.G, bm.B, 0.9f);

            foreach (int bookmark in beatmap.Bookmarks)
            {
                bool coexists = timingTimes.Contains(bookmark);
                float x = (float)(bookmark * pixelsPerMs);

                timingLayer.Add(new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopCentre,
                    Width = 2f,
                    X = x,
                    Y = 0,
                    Height = coexists ? baseline_y : HEIGHT,
                    Colour = bookmarkColour,
                });
            }
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
            float next = Math.Clamp(pixelsPerMs * factor, min_px_per_ms, max_px_per_ms);
            if (Math.Abs(next - pixelsPerMs) < 1e-5f)
                return true;

            pixelsPerMs = next;
            buildObjects(); // object positions/widths depend on the zoom
            return true;
        }

        // --- Selection (osu!lazer-style: click to select, CTRL to toggle, drag for a rubber-band box) ---

        /// <summary>The time (ms) under a screen-space position, in beatmap time.</summary>
        private double timeAt(Vector2 screenPosition) => (ToLocalSpace(screenPosition).X - content.X) / pixelsPerMs;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // In the hitsound lanes, defer to click/drag/up handlers (left cycles, right deletes, drag paints).
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
            // In the hitsound lanes: left-click creates the hitsound, Shift+left-click cycles its bank (N/S/D).
            if (tryLaneAt(e.ScreenSpaceMousePosition, out int lane))
            {
                double laneTime = timeAt(e.ScreenSpaceMousePosition);
                if (e.ShiftPressed)
                    cycleBankAt(laneTime, lane);
                else
                    createCellAt(laneTime, lane);
                return true;
            }

            double time = timeAt(e.ScreenSpaceMousePosition);
            float localY = ToLocalSpace(e.ScreenSpaceMousePosition).Y;
            int hit = objectAt(time, localY);

            if (hit < 0)
            {
                // Clicking empty space clears both selections (unless adding to the object selection).
                if (!e.ControlPressed)
                    selection.Clear();
                nodeSelection.Clear();
            }
            else if (e.ControlPressed)
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

            // Grabbing a slider's tail drags its reverse (repeat) count instead of moving it, like lazer.
            if (e.Button == MouseButton.Left && id >= 0 && isSliderTailGrab(id, startTime))
            {
                if (!selection.Contains(id))
                    selection.SetSingle(id);

                repeatDragging = true;
                actions.BeginSliderRepeatDrag(id);
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
            dragBaseline = e.ControlPressed ? new HashSet<int>(selection.Selected) : new HashSet<int>();

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

            dragBox?.Expire();
            dragBox = null;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            // A right-click (no drag) in the hitsound lanes deletes that cell's addition. A right-DRAG is handled
            // by the paint stroke (hitsoundDragged), so it isn't double-processed here.
            if (e.Button == MouseButton.Right && !hitsoundDragged && tryLaneAt(e.ScreenSpaceMousePosition, out int lane))
                deleteCellAt(timeAt(e.ScreenSpaceMousePosition), lane);

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
    }
}
