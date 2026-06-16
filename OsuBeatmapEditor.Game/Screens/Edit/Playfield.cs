using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Timing;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The osu! Standard play area (512x384 osu!pixels). Fills its parent and keeps the play area
    /// centred and uniformly scaled to fit, rendering each hit object in osu!pixel coordinates.
    /// Hit-object visibility tracks <see cref="TimeSource"/> (the audio position) every frame.
    /// </summary>
    public partial class Playfield : Container
    {
        /// <summary>Supplies the current playback time in milliseconds (typically the audio track).</summary>
        public Func<double>? TimeSource;

        /// <summary>Supplies the beat-snapped current time (for the placement preview's combo number).</summary>
        public Func<double>? SnappedTimeSource;

        /// <summary>Resolves where a circle placement at a cursor position would actually land (distance/magnetic snap).</summary>
        public Func<Vector2, Vector2>? PlacementSnap;

        /// <summary>Spacing (osu!pixels) between slider ticks for a given slider, honouring tempo/SV/tick-rate; 0 = none.</summary>
        public Func<HitObjectModel, double>? SliderTickDistance;


        [Resolved]
        private EditorSelection selection { get; set; } = null!;

        [Resolved]
        private NodeSelection nodeSelection { get; set; } = null!;

        [Resolved]
        private IEditorActions actions { get; set; } = null!;

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private EditableBeatmap editable { get; set; } = null!;

        private bool moving;
        private Vector2 moveStart;

        // Rubber-band box-select state.
        private bool boxSelecting;
        private Vector2 boxStart;
        private Vector2 boxCurrent;
        private Box? selectionBox;
        private HashSet<int> dragBaseline = new HashSet<int>();
        // Objects picked by the box so far. Box selection accumulates: while the box is held during playback,
        // objects that fade in inside it are added and never removed as they fade out again.
        private HashSet<int> boxSelected = new HashSet<int>();

        // Grid sizes (osu!pixels) cycled by the G key, matching osu!lazer's editor; 0 = grid off.
        private static readonly float[] grid_sizes = { 4f, 8f, 16f, 32f, 0f };
        private int gridSizeIndex = 2; // default to 16px, like lazer

        private readonly Container playArea;
        private Container gridContainer = null!;
        private readonly HitObjectLifetimeContainer followPointContainer;
        private readonly HitObjectLifetimeContainer hitObjectContainer;
        private readonly Container selectionLayer;
        private readonly Container overlayLayer;
        private CircularContainer placementPreview = null!;
        private Box placementFill = null!;
        private SpriteText placementNumber = null!;

        // Live drawable + model per object id, so edits sync incrementally instead of rebuilding everything.
        private readonly List<DrawableHitObject> objects = new List<DrawableHitObject>();
        private readonly Dictionary<int, DrawableHitObject> drawableMap = new Dictionary<int, DrawableHitObject>();
        private readonly Dictionary<int, HitObjectModel> modelMap = new Dictionary<int, HitObjectModel>();
        private float lastDiameter = -1;
        private double lastPreempt = -1;

        private IReadOnlyList<HitObjectModel> currentHitObjects = Array.Empty<HitObjectModel>();
        private float currentDiameter = 40f;

        // Mod-preview state: HardRock flips the play area vertically (about its centre, like osu!'s HR), Hidden
        // makes objects fade out before they're hit. Set by the editor; purely visual, never saved.
        private bool modHardRock;
        private bool modHidden;
        // Set by SetMods (HardRock) so the next SetHitObjects rebuilds every drawable exactly once.
        private bool pendingFullRebuild;

        // Auto-mod preview: a cursor that plays the map. Purely visual, like the HR/HD previews.
        private AutoCursor autoCursor = null!;
        private bool modAuto;

        // Each live follow-point connection plus the endpoints/ids it was built from, so a position drag can
        // recreate just the connections touching the selection instead of rebuilding the whole map (which lags).
        private sealed class FollowPointConnection
        {
            public int FromId, ToId;
            public Vector2 BaseStart, BaseEnd;
            public double StartTime, EndTime;
            public DrawableFollowPoints Drawable = null!;
        }

        private readonly List<FollowPointConnection> followPoints = new List<FollowPointConnection>();

        /// <summary>Whether the circle-placement tool is armed (a ghost circle follows the cursor).</summary>
        public bool PlacementActive { get; private set; }

        /// <summary>Whether the slider-placement tool is armed (drag from head to tail to create a linear slider).</summary>
        public bool SliderPlacementActive { get; private set; }

        /// <summary>Whether the spinner-placement tool is armed (click to start, scrub forward, click/right-click to end).</summary>
        public bool SpinnerPlacementActive { get; private set; }

        // Spinner build state: a left-click starts it at the current time; the end follows the playhead as the
        // user scrubs forward; a left- or right-click commits it. Mirrors osu!lazer's SpinnerPlacementBlueprint.
        private bool buildingSpinner;
        private CircularContainer? spinnerGhost;

        /// <summary>Whether the next placed object will start a new combo (toggled with Q).</summary>
        public bool NewComboArmed { get; set; }

        // Slider-build state: anchors committed by successive clicks (double-click = sharp corner),
        // right-click finishes (the cursor becomes the tail), Esc cancels.
        private bool buildingSlider;
        private readonly List<SliderControlPoint> sliderAnchors = new List<SliderControlPoint>();
        private double lastAnchorClickTime = double.MinValue;
        private SmoothPath? sliderPreview;
        // Node markers shown on each anchor (and the cursor) while a slider is being drawn.
        private Container? sliderNodes;

        // Live control-point editor for the currently-selected single slider.
        private SliderControlPointVisualiser? controlPoints;

        // Transform box (rotate/scale/flip), shown while Shift is held with a selection.
        private SelectionBox? transformBox;
        private bool transforming;

        /// <summary>True while a slider is being traced (head placed, awaiting more anchors / finish).</summary>
        public bool BuildingSlider => buildingSlider;

        public Playfield()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = playArea = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH, ParsedBeatmap.PLAYFIELD_HEIGHT),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.BackgroundDark,
                        Alpha = 0.6f,
                    },
                    // Snapping grid (toggled / resized with G), behind everything else.
                    gridContainer = new Container { RelativeSizeAxes = Axes.Both },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = 2,
                        BorderColour = OsuColour.TextMuted,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Transparent,
                        },
                    },
                    // Follow points sit beneath the hit objects. Both are lifetime-managed so only on-screen
                    // objects are realised/updated/drawn (like lazer's HitObjectContainer).
                    followPointContainer = new HitObjectLifetimeContainer { RelativeSizeAxes = Axes.Both },
                    hitObjectContainer = new HitObjectLifetimeContainer { RelativeSizeAxes = Axes.Both },
                    // Persistent yellow selection outlines (always visible, independent of object fade).
                    selectionLayer = new Container { RelativeSizeAxes = Axes.Both },
                    // Placement preview + rubber-band box.
                    overlayLayer = new Container { RelativeSizeAxes = Axes.Both, Child = buildPlacementPreview() },
                    // Auto-mod preview cursor, on top of everything (osu!pixel space = the play area itself).
                    autoCursor = new AutoCursor { PositionSource = autoCursorPosition },
                },
            };

            buildGrid();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            selection.Changed += updateSelection;
            nodeSelection.Changed += updateSelection;

            // Rebuild all objects live when the relevant appearance settings change.
            settings.ObjectBackgroundOpacity.ValueChanged += _ => rebuildAppearance();
            settings.ObjectBorderThickness.ValueChanged += _ => rebuildAppearance();
            settings.SliderTickSize.ValueChanged += _ => rebuildAppearance();
            settings.ObjectFadeOut.ValueChanged += _ => rebuildAppearance();
            // Combo palette changes (editor palette, map colours, or the use-map-colours toggle) all funnel
            // through EditableBeatmap.ColoursChanged - rebuild so objects pick up the new colours live.
            editable.ColoursChanged += rebuildAppearance;
        }

        /// <summary>
        /// Forces every hit-object drawable to be recreated. Used when a global value that the incremental
        /// sync can't see in the per-object models changes (e.g. slider tick rate / velocity affect the ticks).
        /// </summary>
        public void RebuildObjects() => rebuildAppearance();

        /// <summary>
        /// Sets the active mod-preview state. HardRock changes circle size (diameter) + AR (preempt) and flips
        /// the field, so it needs every drawable rebuilt - it just flags the rebuild and lets the caller's
        /// following <c>SetHitObjects</c> do it once (avoids a double rebuild). Hidden only changes the fade, so
        /// it's applied in place on the existing drawables (no recreation) to keep the toggle snappy.
        /// </summary>
        public void SetMods(bool hardRock, bool hidden)
        {
            bool hrChanged = modHardRock != hardRock;
            bool hdChanged = modHidden != hidden;
            if (!hrChanged && !hdChanged)
                return;

            modHardRock = hardRock;
            modHidden = hidden;

            // HardRock alters diameter/preempt → the next SetHitObjects must rebuild everything.
            if (hrChanged)
                pendingFullRebuild = true;

            // Hidden is a pure fade change: retarget the existing drawables instead of recreating them.
            if (hdChanged && !hrChanged)
            {
                foreach (var d in objects)
                    d.SetHidden(hidden);
            }
        }

        /// <summary>Toggles the Auto-mod preview cursor (visual only).</summary>
        public void SetAutoPlay(bool enabled) => modAuto = enabled;

        /// <summary>Sets the Auto cursor colour.</summary>
        public void SetAutoColour(Color4 colour) => autoCursor.SetColour(colour);

        /// <summary>Sets the Auto cursor trail length (number of trailing segments).</summary>
        public void SetAutoTrailLength(int length) => autoCursor.SetTrailLength(length);

        /// <summary>Sets the Auto cursor trail thickness multiplier.</summary>
        public void SetAutoTrailWidth(float width) => autoCursor.SetTrailWidth(width);

        /// <summary>
        /// The osu!pixel position of the Auto cursor at the current playback time, or null when the preview is
        /// off / there are no objects. Ported from the behaviour of osu!lazer's Auto generator: rest on circles,
        /// trace sliders along the path (bouncing across repeats), spin on spinners, glide between objects.
        /// </summary>
        private Vector2? autoCursorPosition()
        {
            if (!modAuto || currentHitObjects.Count == 0)
                return null;

            double time = TimeSource?.Invoke() ?? 0;
            var objs = currentHitObjects;

            // Last object whose start is at/before the current time (binary search; list is time-sorted).
            int lo = 0, hi = objs.Count - 1, prev = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (objs[mid].StartTime <= time)
                {
                    prev = mid;
                    lo = mid + 1;
                }
                else
                    hi = mid - 1;
            }

            // Before the first object: wait on its start position.
            if (prev < 0)
                return autoStartPos(objs[0]);

            var cur = objs[prev];

            // Inside the current object (a long slider / spinner): follow it.
            if (time <= autoEndTime(cur))
                return autoFollowPos(cur, time);

            // After the last object: rest where it ended.
            if (prev + 1 >= objs.Count)
                return autoEndPos(cur);

            // In the gap between two objects: glide from one to the next, easing in/out like Auto does.
            var next = objs[prev + 1];
            double from = autoEndTime(cur);
            double to = next.StartTime;
            if (to <= from)
                return autoStartPos(next);

            double f = Math.Clamp((time - from) / (to - from), 0, 1);
            f = f * f * (3 - 2 * f); // smoothstep
            return Vector2.Lerp(autoEndPos(cur), autoStartPos(next), (float)f);
        }

        /// <summary>Spin speed (rad/ms) and radius (osu!px) the Auto cursor uses on spinners.</summary>
        private const double auto_spin_speed = 0.025;
        private const float auto_spin_radius = 55f;

        private Vector2 autoStartPos(HitObjectModel o)
            => new Vector2(o.X, o.Y) + DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);

        private Vector2 autoEndPos(HitObjectModel o)
        {
            Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
            switch (o.Kind)
            {
                case HitObjectKind.Slider when o.Path is { Count: > 0 } path:
                    // Even slide count ends back at the head; odd ends at the tail.
                    return (o.Slides % 2 == 0 ? path[0] : path[^1]) + stack;

                case HitObjectKind.Spinner:
                    return new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2, ParsedBeatmap.PLAYFIELD_HEIGHT / 2);

                default:
                    return new Vector2(o.X, o.Y) + stack;
            }
        }

        private double autoEndTime(HitObjectModel o) => o.StartTime + Math.Max(0, o.Duration);

        private Vector2 autoFollowPos(HitObjectModel o, double time)
        {
            Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);

            switch (o.Kind)
            {
                case HitObjectKind.Slider when o.Path is { Count: > 0 } path:
                {
                    double dur = Math.Max(1, o.Duration);
                    double spanDur = dur / Math.Max(1, o.Slides);
                    double elapsed = Math.Clamp(time - o.StartTime, 0, dur);
                    int span = spanDur > 0 ? Math.Min(o.Slides - 1, (int)(elapsed / spanDur)) : 0;
                    double within = spanDur > 0 ? (elapsed - span * spanDur) / spanDur : 0;
                    double frac = span % 2 == 0 ? within : 1 - within; // bounce back on each repeat
                    return SliderGeometry.PointAtFraction(path, Math.Clamp(frac, 0, 1)) + stack;
                }

                case HitObjectKind.Spinner:
                {
                    var centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2, ParsedBeatmap.PLAYFIELD_HEIGHT / 2);
                    double ang = (time - o.StartTime) * auto_spin_speed;
                    return centre + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * auto_spin_radius;
                }

                default:
                    return new Vector2(o.X, o.Y) + stack;
            }
        }

        /// <summary>Rebuilds every hit-object drawable so appearance-setting changes take effect immediately.</summary>
        private void rebuildAppearance()
        {
            if (currentHitObjects.Count == 0)
                return;

            hitObjectContainer.Clear();
            drawableMap.Clear();
            modelMap.Clear();
            SetHitObjects(currentHitObjects, currentDiameter, lastPreempt);
        }

        /// <summary>The cursor's position in osu!pixels, if it is currently over the play area.</summary>
        public bool TryGetCursorOsuPosition(out Vector2 osuPosition)
        {
            osuPosition = Vector2.Zero;

            var input = GetContainingInputManager();
            if (input == null)
                return false;

            Vector2 local = playArea.ToLocalSpace(input.CurrentState.Mouse.Position);
            if (!insidePlayfield(local))
                return false;

            osuPosition = local;
            return true;
        }

        /// <summary>
        /// Whether the object with the given id is currently on-screen: the playback time lies within its
        /// approach+active window (StartTime - preempt .. end). Time-based (not the drawable's interpolated
        /// alpha) so it stays consistent with callers that snap against the same <see cref="TimeSource"/>.
        /// </summary>
        public bool IsObjectVisible(int id)
        {
            double t = TimeSource?.Invoke() ?? 0;

            foreach (var o in currentHitObjects)
            {
                if (o.Id != id)
                    continue;

                // Match what's actually drawn (and what selection treats as hittable): the approach window
                // plus the configurable fade-out tail, so a passed object stays snappable while it lingers.
                double end = o.StartTime + (o.Kind is HitObjectKind.Slider or HitObjectKind.Spinner ? o.Duration : 0);
                return t >= o.StartTime - lastPreempt && t <= end + settings.ObjectFadeOut.Value;
            }

            return false;
        }

        /// <summary>The top-most currently-visible object under an osu!pixel position, or null.</summary>
        private DrawableHitObject? hittableAt(Vector2 osuPosition)
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i].IsHittable && objects[i].BodyContains(osuPosition))
                    return objects[i];
            }

            return null;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            // While tracing a slider, right-click finishes it (committing the placed anchors).
            if (e.Button == MouseButton.Right && buildingSlider)
            {
                finishSlider();
                return true;
            }

            // While placing a spinner, right-click commits it (ending at the current playhead time).
            if (e.Button == MouseButton.Right && buildingSpinner)
            {
                finishSpinner();
                return true;
            }

            // A placement tool is armed but nothing has been started yet (we're still just previewing) -
            // right-click returns to the selection tool, like pressing 1.
            if (e.Button == MouseButton.Right && (PlacementActive || SliderPlacementActive || SpinnerPlacementActive))
            {
                SetPlacementActive(false);
                SetSliderPlacementActive(false);
                SetSpinnerPlacementActive(false);
                return true;
            }

            // Right-click quick-delete (M2), like lazer: remove the hovered object or the whole selection.
            if (e.Button == MouseButton.Right)
            {
                var o = hittableAt(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
                if (o != null)
                {
                    if (selection.Contains(o.Id))
                        actions.DeleteSelected();
                    else
                        actions.DeleteObject(o.Id);
                }

                return true;
            }

            return true; // receive the left press for click/drag
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Slider tool: each left-click drops an anchor; a quick double-click turns the last one into a
            // sharp corner (red anchor), matching osu!lazer.
            if (SliderPlacementActive)
            {
                double now = Time.Current;
                bool doubleClick = buildingSlider && now - lastAnchorClickTime < 250;
                lastAnchorClickTime = now;

                if (doubleClick && sliderAnchors.Count > 1)
                    sliderAnchors[^1] = sliderAnchors[^1] with { Type = sliderAnchors[^1].IsSegmentStart ? (SliderPathType?)null : SliderPathType.Bezier };
                else
                    addSliderAnchor(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
                return true;
            }

            // Spinner tool: the first left-click starts the spinner at the current time; a second left-click
            // commits it (the end having followed the playhead). Right-click also commits (handled above).
            if (SpinnerPlacementActive)
            {
                if (!buildingSpinner)
                {
                    buildingSpinner = true;
                    setSpinnerGhostActive(true);
                    actions.BeginSpinnerPlacement();
                }
                else
                {
                    finishSpinner();
                }

                return true;
            }

            // With the placement tool armed, a left-click drops a circle at the cursor instead of selecting.
            if (PlacementActive)
            {
                Vector2 placePos = playArea.ToLocalSpace(e.ScreenSpaceMousePosition);
                if (insidePlayfield(placePos))
                    actions.PlaceCircle(placePos);
                return true;
            }

            // Selecting on the playfield, like lazer's composer: click an object to select it (CTRL toggles),
            // click empty space to clear. Only currently-visible objects are hittable.
            var o = hittableAt(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));

            if (o != null)
            {
                if (e.ControlPressed)
                {
                    selection.Toggle(o.Id);
                    nodeSelection.Clear();
                }
                else
                {
                    // Two-stage selection: the first click selects the whole object. Selecting an individual
                    // slider part (head / tail / body) is only possible once the slider is the sole selection,
                    // and is handled by the SliderControlPointVisualiser that appears in that state.
                    selection.SetSingle(o.Id);
                    nodeSelection.Clear();
                }
            }
            else if (!e.ControlPressed)
            {
                selection.Clear();
                nodeSelection.Clear();
            }

            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            // Slider tool: consume drags so they don't box-select; the anchor is added on release.
            if (SliderPlacementActive)
                return true;

            // Spinner tool: a drag shouldn't box-select; placement is click-based.
            if (SpinnerPlacementActive)
                return true;

            if (PlacementActive)
                return false;

            Vector2 start = playArea.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
            var o = hittableAt(start);

            if (o != null)
            {
                // Dragging an object moves the selection.
                if (!selection.Contains(o.Id))
                    selection.SetSingle(o.Id);

                moving = true;
                moveStart = start;
                actions.BeginMove();
                return true;
            }

            // Dragging empty space rubber-band selects, like lazer's composer.
            boxSelecting = true;
            boxStart = start;
            dragBaseline = e.ControlPressed ? new HashSet<int>(selection.Selected) : new HashSet<int>();
            boxSelected = new HashSet<int>(dragBaseline);

            overlayLayer.Add(selectionBox = new Box
            {
                Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.15f),
            });

            updateBoxSelection(start);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            Vector2 pos = playArea.ToLocalSpace(e.ScreenSpaceMousePosition);

            if (SliderPlacementActive)
                return; // preview tracks the cursor in Update(); the anchor commits on release
            if (moving)
                actions.MoveSelectionPosition(pos - moveStart);
            else if (boxSelecting)
                updateBoxSelection(pos);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (SliderPlacementActive)
            {
                addSliderAnchor(playArea.ToLocalSpace(e.ScreenSpaceMousePosition));
            }
            else if (moving)
            {
                moving = false;
                actions.EndMove();
            }
            else if (boxSelecting)
            {
                boxSelecting = false;
                selectionBox?.Expire();
                selectionBox = null;
            }
        }

        /// <summary>Commits a slider anchor at the given (clamped) position, starting the trace on the first click.</summary>
        private void addSliderAnchor(Vector2 osuPosition)
        {
            Vector2 p = clampToPlayfield(osuPosition);

            if (!buildingSlider)
            {
                buildingSlider = true;
                sliderAnchors.Clear();
                overlayLayer.Add(sliderPreview = new SmoothPath
                {
                    PathRadius = Math.Max(2f, currentDiameter / 2f - 2f),
                    Colour = new Color4(OsuColour.Pink.R, OsuColour.Pink.G, OsuColour.Pink.B, 0.55f),
                });
                // Node markers sit on top of the trace so the anchors stay visible as the slider is drawn.
                overlayLayer.Add(sliderNodes = new Container { RelativeSizeAxes = Axes.Both });
            }

            // Ignore a duplicate anchor right on top of the previous one.
            if (sliderAnchors.Count > 0 && (sliderAnchors[^1].Position - p).LengthSquared < 1f)
                return;

            sliderAnchors.Add(new SliderControlPoint(p));
        }

        /// <summary>Finishes the slider, adding the cursor as the tail, then committing it (needs a head + tail).</summary>
        private void finishSlider()
        {
            if (!buildingSlider)
                return;

            // Where the user right-clicked becomes the final anchor (unless it lands on the previous one).
            if (TryGetCursorOsuPosition(out var cursor))
            {
                Vector2 p = clampToPlayfield(cursor);
                if (sliderAnchors.Count == 0 || (sliderAnchors[^1].Position - p).LengthSquared >= 1f)
                    sliderAnchors.Add(new SliderControlPoint(p));
            }

            var anchors = new List<SliderControlPoint>(sliderAnchors);
            cancelSliderBuild();

            if (anchors.Count >= 2)
                actions.PlaceSlider(anchors);
        }

        /// <summary>Discards the in-progress slider trace and its preview.</summary>
        public void CancelSliderBuild() => cancelSliderBuild();

        private void cancelSliderBuild()
        {
            buildingSlider = false;
            sliderAnchors.Clear();
            sliderPreview?.Expire();
            sliderPreview = null;
            sliderNodes?.Expire();
            sliderNodes = null;
            actions.ClearSliderPreview();
        }

        private static Vector2 clampToPlayfield(Vector2 p) => new Vector2(
            Math.Clamp(p.X, 0, ParsedBeatmap.PLAYFIELD_WIDTH),
            Math.Clamp(p.Y, 0, ParsedBeatmap.PLAYFIELD_HEIGHT));

        private void updateBoxSelection(Vector2 current)
        {
            boxCurrent = current;

            Vector2 min = Vector2.ComponentMin(boxStart, current);
            Vector2 max = Vector2.ComponentMax(boxStart, current);

            if (selectionBox != null)
            {
                selectionBox.Position = min;
                selectionBox.Size = max - min;
            }

            // Accumulate: add every currently-visible object whose head lies inside the box, and never remove.
            // Re-run each frame (from Update) so that while the box is held during playback, objects that fade
            // in inside it get picked up - and ones that fade out stay selected.
            foreach (var o in objects)
            {
                if (!o.IsHittable)
                    continue;

                Vector2 head = o.HeadPosition;
                if (head.X >= min.X && head.X <= max.X && head.Y >= min.Y && head.Y <= max.Y)
                    boxSelected.Add(o.Id);
            }

            selection.SetRange(boxSelected);
        }

        /// <summary>Advances to the next grid size (G key), wrapping back to the start.</summary>
        public void CycleGridSize()
        {
            gridSizeIndex = (gridSizeIndex + 1) % grid_sizes.Length;
            buildGrid();
        }

        private void buildGrid()
        {
            gridContainer.Clear();

            float size = grid_sizes[gridSizeIndex];
            if (size <= 0)
                return;

            float centreX = ParsedBeatmap.PLAYFIELD_WIDTH / 2f;
            float centreY = ParsedBeatmap.PLAYFIELD_HEIGHT / 2f;

            // Vertical lines, stepping out from the playfield centre in both directions.
            for (float x = centreX; x <= ParsedBeatmap.PLAYFIELD_WIDTH; x += size)
            {
                gridContainer.Add(gridLine(x, vertical: true, centre: x == centreX));
                if (x != centreX)
                    gridContainer.Add(gridLine(centreX - (x - centreX), vertical: true, centre: false));
            }

            // Horizontal lines.
            for (float y = centreY; y <= ParsedBeatmap.PLAYFIELD_HEIGHT; y += size)
            {
                gridContainer.Add(gridLine(y, vertical: false, centre: y == centreY));
                if (y != centreY)
                    gridContainer.Add(gridLine(centreY - (y - centreY), vertical: false, centre: false));
            }
        }

        private static Drawable gridLine(float position, bool vertical, bool centre) => new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = vertical ? Anchor.TopCentre : Anchor.CentreLeft,
            RelativeSizeAxes = vertical ? Axes.Y : Axes.X,
            Width = vertical ? (centre ? 2f : 1f) : 1f,
            Height = vertical ? 1f : (centre ? 2f : 1f),
            X = vertical ? position : 0,
            Y = vertical ? 0 : position,
            Colour = new Color4(1f, 1f, 1f, centre ? 0.22f : 0.1f),
        };

        /// <summary>Arms or disarms the circle-placement tool, clearing the selection when arming.</summary>
        public void SetPlacementActive(bool active)
        {
            PlacementActive = active;
            NewComboArmed = false;

            if (active)
            {
                SliderPlacementActive = false;
                SetSpinnerPlacementActive(false);
                cancelSliderBuild();
                selection.Clear();
            }
            else
            {
                placementPreview.Alpha = 0;
            }
        }

        /// <summary>Arms or disarms the spinner-placement tool, clearing the selection when arming.</summary>
        public void SetSpinnerPlacementActive(bool active)
        {
            if (SpinnerPlacementActive == active)
                return;

            SpinnerPlacementActive = active;
            NewComboArmed = false;
            cancelSpinnerBuild();

            if (active)
            {
                PlacementActive = false;
                SliderPlacementActive = false;
                cancelSliderBuild();
                selection.Clear();
                setSpinnerGhostActive(false); // visible-but-dim ghost shows the spinner area until placement starts
                ensureSpinnerGhost();
            }
            else
            {
                placementPreview.Alpha = 0;
                spinnerGhost?.Expire();
                spinnerGhost = null;
            }
        }

        /// <summary>Discards the in-progress spinner placement.</summary>
        public void CancelSpinnerBuild() => cancelSpinnerBuild();

        private void cancelSpinnerBuild()
        {
            if (!buildingSpinner)
                return;

            buildingSpinner = false;
            actions.CancelSpinnerPlacement();
            setSpinnerGhostActive(false);
        }

        private void finishSpinner()
        {
            if (!buildingSpinner)
                return;

            buildingSpinner = false;
            actions.FinishSpinnerPlacement();
            setSpinnerGhostActive(false); // stays armed (dim ghost) for the next spinner
        }

        /// <summary>Creates the centred spinner ghost ring if the tool is armed and it isn't already shown.</summary>
        private void ensureSpinnerGhost()
        {
            if (spinnerGhost != null)
                return;

            const float radius = 130f; // matches DrawableHitObject's spinner disc
            overlayLayer.Add(spinnerGhost = new CircularContainer
            {
                Position = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f),
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Masking = true,
                BorderThickness = 4,
                BorderColour = OsuColour.Pink,
                Alpha = 0.4f,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
            });
        }

        private void setSpinnerGhostActive(bool building)
        {
            ensureSpinnerGhost();
            spinnerGhost?.FadeTo(building ? 0.85f : 0.4f, 150, Easing.OutQuint);
        }

        /// <summary>Arms or disarms the slider-placement tool, clearing the selection when arming.</summary>
        public void SetSliderPlacementActive(bool active)
        {
            SliderPlacementActive = active;
            NewComboArmed = false;
            cancelSliderBuild();

            if (active)
            {
                PlacementActive = false;
                SetSpinnerPlacementActive(false);
                selection.Clear();
            }
            else
            {
                placementPreview.Alpha = 0;
            }
        }

        private static bool insidePlayfield(Vector2 osuPosition) =>
            osuPosition.X >= 0 && osuPosition.Y >= 0
            && osuPosition.X <= ParsedBeatmap.PLAYFIELD_WIDTH && osuPosition.Y <= ParsedBeatmap.PLAYFIELD_HEIGHT;

        /// <summary>
        /// Syncs the displayed hit objects to <paramref name="hitObjects"/>, creating/removing/replacing only
        /// the ones that actually changed (by id + value), so a single edit doesn't rebuild the whole map.
        /// A circle-size (diameter) or AR (preempt) change rebuilds everything, since those affect all objects.
        /// </summary>
        public void SetHitObjects(IReadOnlyList<HitObjectModel> hitObjects, float circleDiameter, double preempt)
        {
            currentHitObjects = hitObjects;
            currentDiameter = circleDiameter;

            bool fullRebuild = pendingFullRebuild || circleDiameter != lastDiameter || preempt != lastPreempt;
            pendingFullRebuild = false;
            lastDiameter = circleDiameter;
            lastPreempt = preempt;

            if (fullRebuild)
            {
                hitObjectContainer.Clear();
                drawableMap.Clear();
                modelMap.Clear();
            }

            var present = new HashSet<int>();

            foreach (var o in hitObjects)
            {
                present.Add(o.Id);

                // Unchanged object: keep its existing drawable (record-struct value equality, paths by ref).
                if (modelMap.TryGetValue(o.Id, out var old) && old.Equals(o))
                    continue;

                if (drawableMap.TryGetValue(o.Id, out var existing))
                    hitObjectContainer.Remove(existing);

                double tick = o.Kind == HitObjectKind.Slider ? SliderTickDistance?.Invoke(o) ?? 0 : 0;
                var drawable = new DrawableHitObject(o, circleDiameter, preempt, settings.ObjectFadeOut.Value, tick, modHidden, modHardRock);
                drawableMap[o.Id] = drawable;
                modelMap[o.Id] = o;
                hitObjectContainer.Add(drawable);
            }

            // Remove drawables whose objects are gone.
            foreach (int id in drawableMap.Keys.Where(id => !present.Contains(id)).ToList())
            {
                hitObjectContainer.Remove(drawableMap[id]);
                drawableMap.Remove(id);
                modelMap.Remove(id);
            }

            // Rebuild the flat list used for hit-testing/selection in time order.
            objects.Clear();
            foreach (var o in hitObjects)
            {
                if (drawableMap.TryGetValue(o.Id, out var d))
                    objects.Add(d);
            }

            buildFollowPoints(hitObjects);
            updateSelection();
        }

        /// <summary>Sets the clock the hit objects animate against (the editor's interpolated audio clock).</summary>
        public void SetClock(IFrameBasedClock clock)
        {
            hitObjectContainer.Clock = clock;
            hitObjectContainer.ProcessCustomClock = false;
            followPointContainer.Clock = clock;
            followPointContainer.ProcessCustomClock = false;
        }

        /// <summary>
        /// Live preview of a position move: translates the selected objects' drawables and their selection
        /// outlines by an osu!pixel offset without rebuilding. Committed (and reset) on drag release.
        /// </summary>
        public void PreviewPositionOffset(Vector2 osuOffset, IReadOnlyDictionary<int, int>? stackHeights = null)
        {
            foreach (var o in objects)
            {
                // Use the live-recomputed stack offset when supplied so the selection visibly stacks mid-drag.
                Vector2 stack = stackHeights != null && stackHeights.TryGetValue(o.Id, out int sh)
                    ? DrawableHitObject.StackOffsetFor(sh, currentDiameter)
                    : o.StackOffset;

                o.Position = selection.Contains(o.Id) ? stack + osuOffset : stack;
            }

            selectionLayer.Position = osuOffset;

            // Keep the slider's control-point handles glued to the body as it is dragged.
            if (controlPoints != null)
                controlPoints.Position = osuOffset;

            // Keep the follow-point chain glued to the moving objects (only the touched connections update).
            previewFollowPoints(osuOffset);
        }

        /// <summary>
        /// Rebuilds the always-visible yellow selection outlines so selected objects stay outlined even
        /// once they fade out of the approach window - matching osu!lazer's editor.
        /// </summary>
        private void updateSelection()
        {
            if (selection == null)
                return;

            selectionLayer.Clear();
            selectionLayer.Position = Vector2.Zero;

            foreach (var o in currentHitObjects)
            {
                if (!selection.Contains(o.Id))
                    continue;

                Vector2 stack = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);

                // Body part selected: highlight the whole path beneath the endpoint rings.
                if (o.Kind == HitObjectKind.Slider && nodeSelection.IsBodySelected(o.Id) && o.Path is { Count: > 1 })
                    selectionLayer.Add(bodyHighlight(o.Path, stack));

                bool headNode = nodeSelection.Selected is { } hn && hn.ObjectId == o.Id && hn.NodeIndex == 0;
                selectionLayer.Add(selectionRing(startPosition(o) + stack, headNode ? EditorTheme.Colours.Error : OsuColour.Yellow));

                if (o.Kind == HitObjectKind.Slider)
                {
                    bool tailNode = nodeSelection.Selected is { } tn && tn.ObjectId == o.Id && tn.NodeIndex == Math.Max(1, o.Slides);
                    selectionLayer.Add(selectionRing(endPosition(o) + stack, tailNode ? EditorTheme.Colours.Error : OsuColour.Yellow));
                }
            }

            updateControlPointEditor();
        }

        /// <summary>
        /// Shows a draggable control-point editor when exactly one slider is selected (and no placement tool
        /// is armed), so its anchors can be moved or toggled red - mirroring osu!lazer's slider selection.
        /// </summary>
        private void updateControlPointEditor()
        {
            controlPoints?.Expire();
            controlPoints = null;

            if (PlacementActive || SliderPlacementActive || selection.Selected.Count != 1)
                return;

            int id = selection.Selected.First();
            foreach (var o in currentHitObjects)
            {
                if (o.Id == id && o.Kind == HitObjectKind.Slider && o.ControlPoints is { Count: >= 2 })
                {
                    int sliderId = o.Id;
                    overlayLayer.Add(controlPoints = new SliderControlPointVisualiser(o, currentDiameter, actions)
                    {
                        // Two-stage part selection: with the slider already the sole selection, clicking a
                        // head/tail node or the body selects just that part.
                        PartNodeClicked = node => nodeSelection.Select(sliderId, node),
                        BodyClicked = () => nodeSelection.SelectBody(sliderId),
                    });
                    return;
                }
            }
        }

        /// <summary>A translucent yellow overlay tracing the slider's body, shown when its body part is selected.</summary>
        private Drawable bodyHighlight(IReadOnlyList<Vector2> path, Vector2 stack)
        {
            var highlight = new SmoothPath
            {
                PathRadius = currentDiameter / 2f,
                Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.3f),
            };
            highlight.Vertices = path;
            highlight.Position = stack - highlight.PositionInBoundingBox(Vector2.Zero);
            return highlight;
        }

        private Drawable selectionRing(Vector2 position, Color4 colour) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(currentDiameter * 1.15f),
            Masking = true,
            BorderThickness = Math.Max(2.5f, currentDiameter * 0.06f),
            BorderColour = colour,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
        };

        /// <summary>
        /// Connects consecutive objects within the same combo (osu! draws no follow point across a new
        /// combo, nor to/from spinners) with a <see cref="DrawableFollowPoints"/> chain.
        /// </summary>
        private void buildFollowPoints(IReadOnlyList<HitObjectModel> hitObjects)
        {
            // Each connection's geometry depends only on its two neighbours, so reuse the ones whose endpoints
            // and times are unchanged and recreate only those that actually moved. A full rebuild every sync
            // would recreate every connection's drawables on each frame of a rotate/scale/move drag, which lags.
            var existing = new Dictionary<(int, int), FollowPointConnection>();
            foreach (var c in followPoints)
                existing[(c.FromId, c.ToId)] = c;

            var rebuilt = new List<FollowPointConnection>();
            var kept = new HashSet<FollowPointConnection>();

            for (int i = 0; i < hitObjects.Count - 1; i++)
            {
                var current = hitObjects[i];
                var next = hitObjects[i + 1];

                // New combo (ComboNumber resets to 1) breaks the chain; spinners never connect.
                if (next.ComboNumber == 1 || current.Kind == HitObjectKind.Spinner || next.Kind == HitObjectKind.Spinner)
                    continue;

                Vector2 start = endPosition(current);
                Vector2 end = startPosition(next);
                double startTime = endTime(current);
                double endTimeMs = next.StartTime;

                // Reuse the existing connection if everything that determines its geometry is unchanged.
                if (existing.TryGetValue((current.Id, next.Id), out var reuse)
                    && reuse.BaseStart == start && reuse.BaseEnd == end
                    && reuse.StartTime == startTime && reuse.EndTime == endTimeMs)
                {
                    kept.Add(reuse);
                    rebuilt.Add(reuse);
                    continue;
                }

                var connection = new FollowPointConnection
                {
                    FromId = current.Id,
                    ToId = next.Id,
                    BaseStart = start,
                    BaseEnd = end,
                    StartTime = startTime,
                    EndTime = endTimeMs,
                };
                connection.Drawable = new DrawableFollowPoints(start, end, startTime, endTimeMs);
                followPointContainer.Add(connection.Drawable);
                rebuilt.Add(connection);
            }

            // Drop connections that no longer exist (or were replaced).
            foreach (var c in followPoints)
                if (!kept.Contains(c))
                    followPointContainer.Remove(c.Drawable);

            followPoints.Clear();
            followPoints.AddRange(rebuilt);
        }

        /// <summary>
        /// Live preview of a position drag for the follow points: recreates only the connections that touch a
        /// selected (dragged) object, offsetting that endpoint by <paramref name="osuOffset"/>. This keeps the
        /// chain glued to the moving objects without rebuilding the whole map's connections each frame (which lags).
        /// A committed move (or drag cancel) calls <see cref="buildFollowPoints"/> again to restore the full set.
        /// </summary>
        private void previewFollowPoints(Vector2 osuOffset)
        {
            foreach (var c in followPoints)
            {
                bool fromSelected = selection.Contains(c.FromId);
                bool toSelected = selection.Contains(c.ToId);
                if (!fromSelected && !toSelected)
                    continue;

                Vector2 start = c.BaseStart + (fromSelected ? osuOffset : Vector2.Zero);
                Vector2 end = c.BaseEnd + (toSelected ? osuOffset : Vector2.Zero);

                followPointContainer.Remove(c.Drawable);
                c.Drawable = new DrawableFollowPoints(start, end, c.StartTime, c.EndTime);
                followPointContainer.Add(c.Drawable);
            }
        }

        private static Vector2 startPosition(HitObjectModel o) =>
            o.Path is { Count: > 0 } path ? path[0] : new Vector2(o.X, o.Y);

        private static Vector2 endPosition(HitObjectModel o)
        {
            // A slider ends at its tail on odd span counts, back at its head on even counts.
            if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } path)
                return o.Slides % 2 == 1 ? path[^1] : path[0];

            return new Vector2(o.X, o.Y);
        }

        private static double endTime(HitObjectModel o) =>
            o.StartTime + (o.Kind == HitObjectKind.Slider ? o.Duration : 0);

        protected override void Update()
        {
            base.Update();

            // Scale the play area exactly like osu!lazer's editor: it fits the 512x384 (4:3) playfield into
            // the full available area (DrawableOsuEditorRuleset overrides the adjustment container to
            // Size = Vector2.One, dropping gameplay's 0.8 shrink). The smaller of width/height binds, keeping
            // the 4:3 aspect; this is a fixed scale independent of circle size.
            // A hair under full-fit so there's a slight breathing margin from the top/bottom timelines.
            float scale = Math.Min(
                DrawWidth / ParsedBeatmap.PLAYFIELD_WIDTH,
                DrawHeight / ParsedBeatmap.PLAYFIELD_HEIGHT) * 0.97f;

            // HardRock flips the play area vertically about its centre (y' = 384 - y), exactly like osu!'s HR.
            // Flipping the whole container keeps input (ToLocalSpace), overlays and selection consistent for
            // free; the upside-down combo numbers are counter-flipped inside the drawables that draw text.
            if (scale > 0)
                playArea.Scale = new Vector2(scale, modHardRock ? -scale : scale);

            // Hit objects + follow points animate themselves via scheduled transforms against the audio clock
            // (set in SetClock); the lifetime container only updates/draws the ones currently on screen.
            updatePlacementPreview();
            updateTransformBox();

            // While a rubber-band box is held, keep picking up objects that fade in inside it during playback,
            // even when the cursor is stationary (so the selection grows as the song plays).
            if (boxSelecting)
                updateBoxSelection(boxCurrent);
        }

        /// <summary>
        /// Shows the rotate/scale/flip selection box while Shift is held with a movable selection (and no
        /// placement tool armed), keeping it sized to the selection's osu!pixel bounds.
        /// </summary>
        private void updateTransformBox()
        {
            var input = GetContainingInputManager();
            bool shift = input?.CurrentState.Keyboard.ShiftPressed ?? false;
            bool eligible = transforming
                            || (shift && !PlacementActive && !SliderPlacementActive && !buildingSlider && actions.SelectionBounds() != null);

            if (eligible && transformBox == null)
            {
                transformBox = new SelectionBox
                {
                    ScreenToOsu = playArea.ToLocalSpace,
                    TransformBegin = () => { transforming = true; actions.BeginSelectionTransform(); },
                    TransformEnd = () => { transforming = false; actions.EndSelectionTransform(); },
                    Rotate = deg => actions.RotateSelection(deg),
                    Resize = (d, a) => actions.ScaleSelection(d, a),
                    Flip = h => actions.FlipSelection(h),
                };
                overlayLayer.Add(transformBox);
            }
            else if (!eligible && transformBox != null)
            {
                transformBox.Expire();
                transformBox = null;
            }

            if (transformBox != null && actions.SelectionBounds() is RectangleF b)
            {
                // Inflate by the circle radius so the box surrounds the visible objects, not just their centres.
                float r = currentDiameter / 2f;
                transformBox.Position = new Vector2(b.X - r, b.Y - r);
                transformBox.Size = new Vector2(b.Width + 2 * r, b.Height + 2 * r);
            }
        }

        /// <summary>A circle preview shaped like a real hit circle (combo colour, white rim, combo number).</summary>
        private Drawable buildPlacementPreview()
        {
            return placementPreview = new CircularContainer
            {
                Origin = Anchor.Centre,
                Masking = true,
                BorderThickness = currentDiameter * 0.08f,
                BorderColour = Color4.White,
                Alpha = 0,
                Children = new Drawable[]
                {
                    placementFill = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Pink, Alpha = 0.9f },
                    placementNumber = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Color4.White,
                        Text = "1",
                    },
                },
            };
        }

        /// <summary>Tracks the ghost circle (and slider trace) to the cursor while a placement tool is armed.</summary>
        private void updatePlacementPreview()
        {
            if ((!PlacementActive && !SliderPlacementActive) || !TryGetCursorOsuPosition(out var pos))
            {
                placementPreview.Alpha = 0;
                if (!SliderPlacementActive)
                    sliderPreview?.Hide();
                return;
            }

            var (colour, number) = previewCombo();

            // Ghost head circle: at the slider's head while tracing, otherwise at the (snapped) cursor.
            Vector2 ghost = buildingSlider && sliderAnchors.Count > 0
                ? sliderAnchors[0].Position
                : PlacementActive && PlacementSnap != null ? PlacementSnap(pos) : pos;

            placementPreview.Size = new Vector2(currentDiameter);
            placementPreview.BorderThickness = currentDiameter * 0.08f;
            placementPreview.Position = ghost;
            placementPreview.Alpha = 0.85f;
            placementFill.Colour = colour;
            placementNumber.Text = number.ToString();
            placementNumber.Font = FontUsage.Default.With(size: currentDiameter * 0.5f, weight: "Bold");
            // Counter-flip the ghost's number so it stays upright while HardRock flips the play area.
            placementNumber.Scale = new Vector2(1, modHardRock ? -1 : 1);

            if (SliderPlacementActive)
                updateSliderTrace(pos);
        }

        /// <summary>Redraws the live slider trace through the committed anchors plus the current cursor.</summary>
        private void updateSliderTrace(Vector2 cursor)
        {
            if (sliderPreview == null || !buildingSlider)
                return;

            var pts = new List<SliderControlPoint>(sliderAnchors) { new SliderControlPoint(clampToPlayfield(cursor)) };
            updateSliderNodes(pts);

            var path = SliderGeometry.ComputePath(SliderGeometry.InferSegmentTypes(pts));

            if (path.Count < 2)
            {
                sliderPreview.Hide();
                actions.ClearSliderPreview();
                return;
            }

            sliderPreview.Show();
            sliderPreview.Vertices = path;
            sliderPreview.Position = -sliderPreview.PositionInBoundingBox(Vector2.Zero);

            // Show, in real time, how long the slider will occupy on the top timeline.
            actions.PreviewSliderPlacement(pts);
        }

        /// <summary>Redraws a small handle on each committed anchor (and the cursor) of the slider being drawn.</summary>
        private void updateSliderNodes(IReadOnlyList<SliderControlPoint> pts)
        {
            if (sliderNodes == null)
                return;

            sliderNodes.Clear();

            float size = Math.Max(6f, currentDiameter * 0.26f);
            for (int i = 0; i < pts.Count; i++)
            {
                bool red = pts[i].IsSegmentStart && i > 0;
                sliderNodes.Add(new CircularContainer
                {
                    Origin = Anchor.Centre,
                    Position = pts[i].Position,
                    Size = new Vector2(size),
                    Masking = true,
                    BorderThickness = 2f,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = red ? OsuColour.Pink : Color4.White,
                        Alpha = red ? 0.95f : 0.85f,
                    },
                });
            }
        }

        /// <summary>The combo colour and number a circle placed at the current time would receive.</summary>
        private (Color4 colour, int number) previewCombo()
        {
            double time = SnappedTimeSource?.Invoke() ?? TimeSource?.Invoke() ?? 0;

            int prevNumber = 0;
            int prevIndex = 0;
            bool anyBefore = false;

            foreach (var o in currentHitObjects)
            {
                if (o.StartTime <= time)
                {
                    prevNumber = o.ComboNumber;
                    prevIndex = o.ComboIndex;
                    anyBefore = true;
                }
                else
                {
                    break;
                }
            }

            // A new combo (Q) or the very first object restarts the count on the next colour.
            if (NewComboArmed || !anyBefore)
                return (comboColour(anyBefore ? prevIndex + 1 : 0), 1);

            return (comboColour(prevIndex), prevNumber + 1);
        }

        /// <summary>The configurable combo colour for an index, as an osuTK colour.</summary>
        private Color4 comboColour(int index)
        {
            var c = editable.ComboColourFor(index);
            return new Color4(c.R, c.G, c.B, c.A);
        }
    }
}
