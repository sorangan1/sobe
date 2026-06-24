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

        // Optional K1/K2 "tapping" overlay shown alongside the Auto cursor (a setting under the AU chip).
        private KeyOverlay keyOverlay = null!;
        private bool modKeyOverlay;
        // "Humanise" the Auto cursor: arc between objects, overshoot + correct, jitter and aim slightly off-centre.
        private bool modHumanize;
        // The object index the key overlay is currently lighting (so a new object bumps the count once).
        private int lastKeyObjectIndex = -1;

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
        // The playhead time when the head was placed. Locked in so scrubbing the timeline while adding anchors
        // doesn't move/re-snap the slider (its start is already established).
        private double sliderBuildStartTime;
        private readonly List<SliderControlPoint> sliderAnchors = new List<SliderControlPoint>();
        private double lastAnchorClickTime = double.MinValue;
        // The live placement preview renders the *actual* slider body (same SliderBodyPath as a committed
        // slider) plus a ghost tail circle, so you see the finished slider while drawing it (lazer-style).
        private SliderBodyPath? sliderPreview;
        private CircularContainer? sliderTailPreview;
        // Node markers shown on each anchor (and the cursor) while a slider is being drawn.
        private Container? sliderNodes;

        // Live control-point editor for the currently-selected single slider, plus its base position (the
        // slider's stack offset) so a move preview can add the drag offset on top without losing the stacking.
        private SliderControlPointVisualiser? controlPoints;
        private Vector2 controlPointsBase;

        // Transform box (rotate/scale/flip), shown while Shift is held with a selection.
        private SelectionBox? transformBox;
        private bool transforming;

        /// <summary>True while a slider is being traced (head placed, awaiting more anchors / finish).</summary>
        public bool BuildingSlider => buildingSlider;

        public Playfield()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
            // The Auto "tapping" overlay floats at the right edge of the play area (screen space), like lazer.
            keyOverlay = new KeyOverlay
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Margin = new MarginPadding { Right = 12 },
                Alpha = 0,
            },
            playArea = new Container
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

        /// <summary>When set, objects are tinted by who placed them (authorship mode) instead of by combo colour.</summary>
        public Func<HitObjectModel, Color4?>? AuthorColourProvider;

        /// <summary>Sets (or clears) the per-object author-colour provider and repaints every object once.</summary>
        public void SetAuthorColouring(Func<HitObjectModel, Color4?>? provider)
        {
            AuthorColourProvider = provider;
            rebuildAppearance();
        }

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

        /// <summary>Toggles the "humanised" Auto cursor (arcs, overshoot, jitter, aim error). Visual only.</summary>
        public void SetHumanize(bool enabled) => modHumanize = enabled;

        /// <summary>Toggles the K1/K2 "tapping" overlay shown alongside the Auto cursor (visual only).</summary>
        public void SetKeyOverlay(bool enabled)
        {
            modKeyOverlay = enabled;
            if (!enabled)
            {
                lastKeyObjectIndex = -1;
                keyOverlay.Reset();
            }
        }

        /// <summary>How long after a circle's start the key stays lit, so a tap is visible (sliders/spinners use their own duration).</summary>
        private const double key_hold_ms = 80;

        /// <summary>
        /// Drives the Auto key overlay from the current playback time: finds the object being "tapped" (the last one
        /// whose hit window covers now), alternates K1/K2 per object like a real player, and bumps the count on each
        /// new object. Only visible while both Auto and the overlay setting are on.
        /// </summary>
        private void updateKeyOverlay()
        {
            bool show = modAuto && modKeyOverlay && currentHitObjects.Count > 0;
            keyOverlay.Alpha = show ? 1 : 0;

            if (!show)
            {
                if (lastKeyObjectIndex != -1)
                {
                    lastKeyObjectIndex = -1;
                    keyOverlay.SetHeld(-1);
                }
                return;
            }

            double time = TimeSource?.Invoke() ?? 0;
            var objs = currentHitObjects;

            // Last object whose start is at/before now (binary search; the list is time-sorted).
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

            // Active only while now is within the object's hit window (its duration, or a short window for circles).
            int active = -1;
            if (prev >= 0)
            {
                double end = objs[prev].StartTime + Math.Max(objs[prev].Duration, key_hold_ms);
                if (time <= end)
                    active = prev;
            }

            if (active != lastKeyObjectIndex)
            {
                lastKeyObjectIndex = active;
                if (active >= 0)
                    keyOverlay.Press(active & 1); // alternate K1/K2 per object
            }

            keyOverlay.SetHeld(active >= 0 ? active & 1 : -1);
        }

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

            autoHold = 0f;
            Vector2 pos = autoBasePosition(prev, time, objs);

            // Humanise: add a continuous, low-amplitude wobble on top of whatever base position we landed on
            // (resting on an object, following a slider, or gliding through a gap). The gap/aim humanisation
            // is folded into the base position below. The wobble is damped on a stack, where a real player keeps
            // the hand planted instead of drifting.
            // Damp the wobble on a stack, but not to zero: replay analysis shows the hand still drifts ~0.4 of a
            // radius across a stacked pair (mostly the per-note aim error), so it is steadied, not frozen.
            if (modHumanize)
                pos += humanJitter(time) * (HumanizeTuning.JitterAmount * (1f - HumanizeTuning.JitterSteadyDamp * autoHold));

            return pos;
        }

        /// <summary>How "planted" the cursor is this frame (1 = sitting on a stack, 0 = moving). Damps the jitter.</summary>
        private float autoHold;

        /// <summary>The exact (un-jittered) Auto position for the current time, given the index of the last started object.</summary>
        private Vector2 autoBasePosition(int prev, double time, IReadOnlyList<HitObjectModel> objs)
        {
            // Before the first object: wait on its start position.
            if (prev < 0)
                return autoStartPos(objs[0]);

            var cur = objs[prev];

            // When humanising, a player lets go of a slider slightly before its true end (and flicks out with the
            // slider's momentum). curEnd is that early release time; the gap to the next object starts from it.
            double curEnd = modHumanize ? humanReleaseTime(cur) : autoEndTime(cur);

            // Inside the current object (a long slider / spinner): follow it.
            if (time <= curEnd)
                return autoFollowPos(cur, time);

            // After the last object: rest where it ended.
            if (prev + 1 >= objs.Count)
                return autoEndPos(cur);

            // In the gap between two objects: glide from one to the next.
            var next = objs[prev + 1];
            double from = curEnd;
            double to = next.StartTime;
            if (to <= from)
                return autoStartPos(next);

            bool sliderExit = modHumanize && cur.Kind == HitObjectKind.Slider;
            Vector2 a = sliderExit ? autoFollowPos(cur, curEnd) : autoEndPos(cur);
            Vector2 b = autoStartPos(next);
            double f = Math.Clamp((time - from) / (to - from), 0, 1);

            // Humanised gap: flow through tight spacing (streams / close jumps) on a curve, overshoot + correct
            // only on very large jumps. Otherwise the perfect Auto easing (smoothstep) along a straight line.
            if (modHumanize)
            {
                // Leaving a slider, the "before" tangent points along the slider so the cursor carries its momentum out.
                Vector2 sliderBefore = sliderExit ? autoFollowPos(cur, Math.Max(cur.StartTime, curEnd - 24)) : a;
                return humanGapPosition(objs, prev, sliderExit, a, b, sliderBefore, f, to - from);
            }

            f = f * f * (3 - 2 * f); // smoothstep
            return Vector2.Lerp(a, b, (float)f);
        }

        /// <summary>
        /// When humanising, the time a player effectively lets go of an object. Replay analysis shows real players
        /// stay on the slider almost to the end (median lead ~2.5ms, ~1% of duration) - the follow circle lets the
        /// cursor drift off the ball without dropping tracking - so the release is tiny, not the big lead I first guessed.
        /// </summary>
        private double humanReleaseTime(HitObjectModel o)
        {
            if (o.Kind != HitObjectKind.Slider)
                return autoEndTime(o);
            double dur = Math.Max(0, o.Duration);
            return autoEndTime(o) - Math.Min(dur * HumanizeTuning.SliderReleaseFrac, HumanizeTuning.SliderReleaseMaxMs);
        }

        /// <summary>
        /// A "human" glide from <paramref name="a"/> to the next object's start over normalised progress <paramref name="f"/>.
        /// The spacing decides the feel, exactly like osu!'s aim metric (distance normalised to a 50px radius, one diameter = 100):
        /// <list type="bullet">
        /// <item>Tight spacing (streams) → high <c>flow</c>: an approximating B-spline through the run of notes at near-constant
        /// velocity, so the cursor sweeps smoothly NEAR each note (like a slider) instead of snapping onto each one.</item>
        /// <item>Medium straight jumps → <c>overshoot</c>: a straight ease-out-back flick that passes the target then corrects.</item>
        /// <item>Back-and-forth jumps → a figure-of-eight bow; large jumps → a plain straight smoothstep onto the circle.</item>
        /// </list>
        /// The B-spline is C2-continuous across gaps, so a whole stream joins into one flowing curve with no seams.
        /// </summary>
        private Vector2 humanGapPosition(IReadOnlyList<HitObjectModel> objs, int prev, bool sliderExit, Vector2 a, Vector2 b, Vector2 sliderBefore, double f, double gapMs)
        {
            float radius = currentDiameter * 0.5f;
            float scaling = radius > 0.01f ? 50f / radius : 1f; // osu!'s normalised_radius = 50
            float dNorm = (b - a).Length * scaling;             // jump distance in osu! normalised units (diameter = 100)

            // Stacked notes sit a few px apart (stack offset ~0.05 diameter each). A real player keeps the cursor
            // planted on the stack and just taps, so we hold position and skip the curve entirely there.
            // CRITICAL: real streams are spaced ~45-90 normalised (replay analysis), so the stack range must stay
            // well below that - otherwise streams get a partial "hold" and jerk note-to-note instead of flowing.
            float stack = 1f - smoothstep(dNorm, HumanizeTuning.StackLo, HumanizeTuning.StackHi);

            // Flow = how much this is a stream (smooth sweep) vs a jump (straight). What makes a stream is the RHYTHM,
            // not the spacing: a run of short, regular note-to-note time gaps flows even when spaced far apart ("spaced
            // streams"). So flow is the max of a spatial term (tight spacing) and a TIME term (fast + regular run).
            float spatialFlow = 1f - smoothstep(dNorm, HumanizeTuning.SpatialFlowLo, HumanizeTuning.SpatialFlowHi);
            float flow = Math.Max(spatialFlow, streamTimeFactor(objs, prev, sliderExit, gapMs));

            // Steady the wobble on stacks AND through fast streams (the hand isn't shaky mid-stream); damps the jitter.
            autoHold = Math.Max(stack, flow);

            // How much the motion reverses (1 = doubles straight back). Computed up front because it splits the two
            // medium-jump behaviours: a straight flick overshoots, a back-and-forth loops (figure-of-eight) instead.
            Vector2 ctxBefore = prev - 1 >= 0 ? autoStartPos(objs[prev - 1]) : a;
            Vector2 ctxAfter = prev + 2 < objs.Count ? autoStartPos(objs[prev + 2]) : b;
            Vector2 inDir = safeNormalize(a - ctxBefore);
            Vector2 outDir = safeNormalize(ctxAfter - b);
            float reversal = Math.Clamp(-Vector2.Dot(inDir, outDir), 0f, 1f);

            // Overshoot + correct: a subtle flick past the target on clear straight jumps only. Gated by (1-flow) so a
            // spaced stream (high flow despite large spacing) keeps flowing instead of overshooting each note.
            float overshoot = smoothstep(dNorm, HumanizeTuning.OvershootOnLo, HumanizeTuning.OvershootOnHi)
                * (1f - smoothstep(dNorm, HumanizeTuning.OvershootOffLo, HumanizeTuning.OvershootOffHi))
                * (1f - HumanizeTuning.OvershootReversalGate * reversal) * (1f - flow);

            // Stream (B-spline) control points use CLEAN centres - no per-note aim error - so the smooth sweep doesn't
            // weave note-to-note. Leaving a slider, the entry comes from the slider so the cursor carries its momentum.
            Vector2 cBefore = sliderExit ? sliderBefore : cleanNode(objs, prev - 1);
            Vector2 cA = sliderExit ? a : cleanNode(objs, prev);
            Vector2 cB = cleanNode(objs, prev + 1);
            Vector2 cAfter = cleanNode(objs, prev + 2);

            double linear = f;
            double smooth = f * f * (3 - 2 * f);
            double timing = smooth;
            timing = lerp(timing, linear, flow);                 // streams glide through at steady speed
            timing = lerp(timing, humanEaseOutBack(f), overshoot); // medium straight jumps flick past then settle
            timing = lerp(timing, stackHold(f), stack);          // a stack stays put, then settles at the very end

            // Jump path: a straight line (with the target's aim error) that lands on the circle (overshoots past it
            // when timing > 1). Stream path: a cubic B-spline that APPROXIMATES the clean centres (passes near, not
            // through) so a 1/4 stream reads as one smooth slider-like sweep instead of snapping to each circle.
            Vector2 straight = Vector2.Lerp(a, b, (float)timing);
            Vector2 curved = bspline(cBefore, cA, cB, cAfter, (float)timing);
            Vector2 pos = Vector2.Lerp(straight, curved, flow * (1f - stack)); // streams flow, jumps stay straight

            // Perpendicular bow on jumps. Real aim arcs to the OUTSIDE of corners, and the side is set by the path's
            // turn direction (geometry) - NOT an alternating index - so a rotational pattern (e.g. a clockwise
            // triangle) keeps every arc on the same outer side and never flips inward between jumps. A pure
            // back-and-forth has no net turn, so there we fall back to an alternating figure-of-eight. Off on streams
            // (the B-spline already shapes them) and stacks.
            Vector2 chord = b - a;
            float chordLen = chord.Length;
            if (chordLen > 0.001f)
            {
                Vector2 prevDir = safeNormalize(a - ctxBefore);
                Vector2 thisDir = chord / chordLen;
                Vector2 nextDir = safeNormalize(ctxAfter - b);
                float turn = cross(prevDir, thisDir) + cross(thisDir, nextDir); // signed total turn through this gap
                float turnMag = Math.Abs(turn);

                float sign, arcAmt;
                if (turnMag > HumanizeTuning.ArcTurnThreshold)
                {
                    sign = turn > 0 ? -1f : 1f;            // bow to the outside of the turn (opposite its direction)
                    arcAmt = HumanizeTuning.ArcOutsideAmount * Math.Min(1f, turnMag);
                }
                else
                {
                    sign = (prev & 1) == 0 ? 1f : -1f;     // ambiguous reversal → alternating figure-of-eight
                    arcAmt = HumanizeTuning.ArcFigure8Amount * reversal;
                }

                float bowWeight = arcAmt * (1f - stack) * (1f - flow);
                if (bowWeight > 0.001f)
                {
                    Vector2 perp = new Vector2(-chord.Y, chord.X) / chordLen;
                    pos += perp * ((float)Math.Sin(f * Math.PI) * bowWeight * Math.Min(chordLen, HumanizeTuning.ArcMaxPx) * sign);
                }
            }

            // Relax toward the playfield centre during a long pause (e.g. after a fast burst), then carry on to the
            // target. Peaks mid-gap and vanishes at the ends, so the endpoints stay put.
            float centrePull = smoothstep((float)gapMs, HumanizeTuning.CentreDriftSlowLo, HumanizeTuning.CentreDriftSlowHi) * (1f - stack);
            if (centrePull > 0.001f)
            {
                Vector2 centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f);
                pos = Vector2.Lerp(pos, centre, (float)Math.Sin(f * Math.PI) * HumanizeTuning.CentreDriftAmount * centrePull);
            }

            return pos;
        }

        private static Vector2 safeNormalize(Vector2 v)
        {
            float l = v.Length;
            return l > 0.0001f ? v / l : Vector2.Zero;
        }

        /// <summary>2D cross product (z of a×b); its sign is the turn direction from <paramref name="a"/> to <paramref name="b"/>.</summary>
        private static float cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// How stream-like this gap is by RHYTHM (0..1): a run of short, regular note-to-note time gaps between circles.
        /// This is what makes even a wide-spaced ("spaced") stream flow smoothly - the cadence, not the spacing. Zero
        /// when leaving a slider, when either side isn't a circle, or when the gap is slow/irregular (a real jump).
        /// </summary>
        private static float streamTimeFactor(IReadOnlyList<HitObjectModel> objs, int prev, bool sliderExit, double gapMs)
        {
            if (sliderExit || objs[prev].Kind != HitObjectKind.Circle || objs[prev + 1].Kind != HitObjectKind.Circle)
                return 0f;

            double gThis = Math.Max(1, gapMs);
            double gPrev = prev - 1 >= 0 ? objs[prev].StartTime - objs[prev - 1].StartTime : gThis;
            double gNext = prev + 2 < objs.Count ? objs[prev + 2].StartTime - objs[prev + 1].StartTime : gThis;

            float shortGap = 1f - smoothstep((float)gThis, HumanizeTuning.StreamGapFastMs, HumanizeTuning.StreamGapSlowMs);
            return shortGap * regularity(gThis, gPrev) * regularity(gThis, gNext);
        }

        /// <summary>1 when two time gaps are close (a regular run), falling to 0 as they diverge (see StreamRegular Lo/Hi).</summary>
        private static float regularity(double a, double b)
        {
            double m = Math.Max(a, b);
            if (m < 1) return 1f;
            return 1f - smoothstep((float)(Math.Abs(a - b) / m), HumanizeTuning.StreamRegularLo, HumanizeTuning.StreamRegularHi);
        }

        /// <summary>An object's start centre WITHOUT aim error (just position + stack), clamped so callers can ask past the ends.
        /// Used for the stream B-spline control points so the smooth sweep isn't jittered by each note's aim offset.</summary>
        private Vector2 cleanNode(IReadOnlyList<HitObjectModel> objs, int k)
        {
            var o = objs[Math.Clamp(k, 0, objs.Count - 1)];
            return autoStartPos(o) - aimError(o);
        }

        /// <summary>Stays at 0 for most of the gap, then ramps to 1 near the end — the cursor holds on a stack and only moves to settle.</summary>
        private static double stackHold(double f) => smoothstep((float)f, HumanizeTuning.StackHoldStart, 1f);

        /// <summary>
        /// Uniform cubic B-spline point at <paramref name="t"/> on the p1→p2 segment. Unlike Catmull-Rom it does NOT pass
        /// through the control points — it approximates them — so a run of stream notes becomes one smooth sweep that
        /// flows near each note instead of snapping to it. C2-continuous across segments (endpoints depend only on the
        /// shared control points), so the whole stream joins into a single flowing curve.
        /// </summary>
        private static Vector2 bspline(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float b0 = (1f - 3f * t + 3f * t2 - t3) / 6f;
            float b1 = (4f - 6f * t2 + 3f * t3) / 6f;
            float b2 = (1f + 3f * t + 3f * t2 - 3f * t3) / 6f;
            float b3 = t3 / 6f;
            return p0 * b0 + p1 * b1 + p2 * b2 + p3 * b3;
        }

        /// <summary>Smoothstep ramp from 0 (at <paramref name="edge0"/>) to 1 (at <paramref name="edge1"/>).</summary>
        private static float smoothstep(float x, float edge0, float edge1)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static double lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Ease-out-back: accelerates, overshoots the target a little, then settles exactly on it at f=1.
        /// The overshoot magnitude is <see cref="HumanizeTuning.OvershootAmount"/> (c1); higher = flies further past.</summary>
        private static double humanEaseOutBack(double f)
        {
            double c1 = HumanizeTuning.OvershootAmount;
            double c3 = c1 + 1;
            double t = f - 1;
            return 1 + c3 * t * t * t + c1 * t * t;
        }

        /// <summary>A faint multi-frequency wobble (osu!px) — the barely-unsteady hand. Replay analysis shows real
        /// streams shake only ~0.27px (median), so this is kept very small. ~0.13px amplitude.</summary>
        private static Vector2 humanJitter(double time)
        {
            double t = time / 1000.0;
            float x = (float)(Math.Sin(t * 9.0) * 0.085 + Math.Sin(t * 19.0) * 0.045);
            float y = (float)(Math.Cos(t * 7.5) * 0.085 + Math.Sin(t * 21.0) * 0.045);
            return new Vector2(x, y);
        }

        /// <summary>
        /// A stable per-object aim error (osu!px) for circles when humanising: the cursor lands a little off the
        /// centre instead of dead-on. Derived from the object's id so it's identical every pass. Zero otherwise.
        /// </summary>
        private Vector2 aimError(HitObjectModel o)
        {
            if (!modHumanize || o.Kind != HitObjectKind.Circle)
                return Vector2.Zero;

            uint h = (uint)o.Id * 2654435761u + 40503u;
            float ang = (h & 0xFFFF) / 65535f * (float)(Math.PI * 2);
            float mag = ((h >> 16) & 0xFFFF) / 65535f;
            // Replay analysis: real aim error is a median ~0.33 of the radius (factor 0.6 x uniform mag ≈ that median).
            float radius = currentDiameter * 0.5f * HumanizeTuning.AimErrorAmount * mag;
            return new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * radius;
        }

        /// <summary>Spin speed (rad/ms) and radius (osu!px) the Auto cursor uses on spinners.</summary>
        private const double auto_spin_speed = 0.025;
        private const float auto_spin_radius = 55f;

        private Vector2 autoStartPos(HitObjectModel o)
            => new Vector2(o.X, o.Y) + DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter) + aimError(o);

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
                    return new Vector2(o.X, o.Y) + stack + aimError(o);
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
                    Vector2 exact = SliderGeometry.PointAtFraction(path, Math.Clamp(frac, 0, 1)) + stack;

                    if (!modHumanize)
                        return exact;

                    // Players don't trace the slider shape 1:1 - they cut curvature, staying inside the path within the
                    // follow circle. Average the path over a small fraction window to round off the curve, and fade the
                    // laziness to 0 at each span boundary (head/repeats/tail are still hit on-path).
                    Vector2 avg = Vector2.Zero;
                    float wsum = 0;
                    for (int k = -2; k <= 2; k++)
                    {
                        float w = 3 - Math.Abs(k);
                        double fr = Math.Clamp(frac + k * 0.05, 0, 1);
                        avg += (SliderGeometry.PointAtFraction(path, fr) + stack) * w;
                        wsum += w;
                    }
                    float lazy = HumanizeTuning.SliderLaziness * (float)Math.Sin(within * Math.PI);
                    return Vector2.Lerp(exact, avg / wsum, lazy);
                }

                case HitObjectKind.Spinner:
                {
                    var centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2, ParsedBeatmap.PLAYFIELD_HEIGHT / 2);
                    double ang = (time - o.StartTime) * auto_spin_speed;
                    return centre + new Vector2((float)Math.Cos(ang), (float)Math.Sin(ang)) * auto_spin_radius;
                }

                default:
                    return new Vector2(o.X, o.Y) + stack + aimError(o);
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
            foreach (var o in currentHitObjects)
            {
                if (o.Id == id)
                    return IsObjectVisible(o);
            }

            return false;
        }

        /// <summary>
        /// Whether the object is currently within the drawn window, computed directly from the model (no lookup).
        /// Prefer this overload in hot paths that already hold the object - the id overload scans to find it.
        /// </summary>
        public bool IsObjectVisible(HitObjectModel o)
        {
            double t = TimeSource?.Invoke() ?? 0;

            // Match what's actually drawn (and what selection treats as hittable): the approach window
            // plus the configurable fade-out tail, so a passed object stays snappable while it lingers.
            double end = o.StartTime + (o.Kind is HitObjectKind.Slider or HitObjectKind.Spinner ? o.Duration : 0);
            return t >= o.StartTime - lastPreempt && t <= end + settings.ObjectFadeOut.Value;
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
            // right-click toggles "new combo" for the next placed object (the phantom preview reflects it).
            if (e.Button == MouseButton.Right && (PlacementActive || SliderPlacementActive || SpinnerPlacementActive))
            {
                NewComboArmed = !NewComboArmed;
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
                if (Shortcut.CommandPressed(e))
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
            else if (!Shortcut.CommandPressed(e))
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
            dragBaseline = Shortcut.CommandPressed(e) ? new HashSet<int>(selection.Selected) : new HashSet<int>();
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
            // The head (the first anchor) snaps exactly like a placed circle (distance + magnetic snap); the
            // later anchors are placed freely where clicked.
            bool isHead = !buildingSlider;
            Vector2 p = clampToPlayfield(isHead && PlacementSnap != null ? PlacementSnap(osuPosition) : osuPosition);

            if (!buildingSlider)
            {
                buildingSlider = true;
                sliderBuildStartTime = snappedNow();
                actions.BeginSliderPlacement();
                sliderAnchors.Clear();

                // The body uses the same SliderBodyPath + appearance settings as a committed slider, so the
                // preview is the finished slider rather than a flat trace.
                Color4 combo = previewCombo().colour;
                overlayLayer.Add(sliderPreview = new SliderBodyPath
                {
                    PathRadius = currentDiameter / 2f,
                    BorderColour = Color4.White,
                    AccentColour = combo,
                    BodyOpacity = settings.ObjectBackgroundOpacity.Value,
                    BorderPortion = Math.Clamp(2f * settings.ObjectBorderThickness.Value, 0.02f, 0.9f),
                });
                // A ghost tail ring (matching the head ghost) sits at the slider's finalized end.
                overlayLayer.Add(sliderTailPreview = ghostRing(combo));
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

            // Commit BEFORE tearing down the build state: PlaceSlider reads the locked-in start time, which
            // cancelSliderBuild clears via EndSliderPlacement.
            if (anchors.Count >= 2)
                actions.PlaceSlider(anchors);

            cancelSliderBuild();
        }

        /// <summary>Discards the in-progress slider trace and its preview.</summary>
        public void CancelSliderBuild() => cancelSliderBuild();

        private void cancelSliderBuild()
        {
            buildingSlider = false;
            sliderAnchors.Clear();
            sliderPreview?.Expire();
            sliderPreview = null;
            sliderTailPreview?.Expire();
            sliderTailPreview = null;
            sliderNodes?.Expire();
            sliderNodes = null;
            clearPreviewFollowPoints();
            actions.ClearSliderPreview();
            actions.EndSliderPlacement();
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
                Color4? author = AuthorColourProvider?.Invoke(o);
                var drawable = new DrawableHitObject(o, circleDiameter, preempt, settings.ObjectFadeOut.Value, tick, modHidden, modHardRock, author);
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

            // Keep the slider's control-point handles glued to the body as it is dragged (on top of the
            // slider's stack offset, which is the visualiser's resting position).
            if (controlPoints != null)
                controlPoints.Position = controlPointsBase + osuOffset;

            // Keep the follow-point chain glued to the moving objects (only the touched connections update).
            previewFollowPoints(osuOffset);
        }

        /// <summary>
        /// Flashes the selection outlines red to signal a transform was refused - e.g. a rotation that would have
        /// pushed a slider out of bounds (which previously got "fixed" by clamping/resizing it, compounding into
        /// runaway growth). A quick visual "no" instead of silently distorting the slider.
        /// </summary>
        public void FlashSelectionBlocked() =>
            selectionLayer.FlashColour(new Color4(1f, 0.15f, 0.15f, 1f), 450, Easing.OutQuint);

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
                    controlPointsBase = DrawableHitObject.StackOffsetFor(o.StackHeight, currentDiameter);
                    overlayLayer.Add(controlPoints = new SliderControlPointVisualiser(o, currentDiameter, actions)
                    {
                        // Shift the whole editor by the slider's stack offset so its handles/polygon line up with
                        // the stacked body (the control points themselves stay in unstacked coordinates, which is
                        // what gets edited - ToLocalSpace then maps clicks/drags back through this offset).
                        Position = controlPointsBase,
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

        // Live follow-point connections from/to the object currently being placed (the ghost preview). Rebuilt
        // each frame in updatePlacementPreview, cleared when no placement tool is active. Lets the mapper see the
        // flow into (and out of) the pending object in real time, exactly like osu!lazer (which inserts the
        // pending object into the beatmap during placement, so its follow points update naturally).
        private readonly List<Drawable> previewFollowDrawables = new List<Drawable>();

        private void clearPreviewFollowPoints()
        {
            if (previewFollowDrawables.Count == 0)
                return;

            foreach (var d in previewFollowDrawables)
                followPointContainer.Remove(d);
            previewFollowDrawables.Clear();
        }

        /// <summary>Whether an object's raw line carries the new-combo type bit (bit 2), i.e. it starts its own combo.</summary>
        private static bool startsNewCombo(HitObjectModel o)
        {
            string[] parts = o.RawLine.Split(',');
            return parts.Length > 3 && int.TryParse(parts[3], out int type) && (type & 4) != 0;
        }

        /// <summary>
        /// Rebuilds the follow points touching the pending placement object: one from the previous in-combo
        /// object into the pending start, and one from the pending end out to the next in-combo object. Spinners
        /// and new-combo boundaries break the chain (as in <see cref="buildFollowPoints"/>).
        /// </summary>
        private void updatePreviewFollowPoints(Vector2 startPos, Vector2 endPos, double startTime, double endTimeMs, bool isNewCombo)
        {
            clearPreviewFollowPoints();

            // Immediate neighbours by time (currentHitObjects is time-sorted). Objects sharing the exact start
            // time are ignored - placing would replace them (removeObjectsAt).
            HitObjectModel? prev = null, next = null;
            foreach (var o in currentHitObjects)
            {
                if (o.StartTime < startTime) prev = o;
                else if (o.StartTime > startTime && next == null) next = o;
            }

            // Incoming: previous -> pending (unless the pending object starts a new combo, or the previous is a spinner).
            if (!isNewCombo && prev is HitObjectModel p && p.Kind != HitObjectKind.Spinner)
                addPreviewFollow(endPosition(p), startPos, endTime(p), startTime);

            // Outgoing: pending -> next (unless the next object starts its own new combo, or is a spinner).
            if (next is HitObjectModel n && n.Kind != HitObjectKind.Spinner && !startsNewCombo(n))
                addPreviewFollow(endPos, startPosition(n), endTimeMs, n.StartTime);
        }

        private void addPreviewFollow(Vector2 start, Vector2 end, double startTime, double endTimeMs)
        {
            var d = new DrawableFollowPoints(start, end, startTime, endTimeMs);
            followPointContainer.Add(d);
            previewFollowDrawables.Add(d);
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
            updateKeyOverlay();

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
                {
                    sliderPreview?.Hide();
                    if (sliderTailPreview != null) sliderTailPreview.Alpha = 0;
                }
                clearPreviewFollowPoints();
                return;
            }

            var (colour, number) = previewCombo();

            // Ghost head circle: at the slider's (already-snapped) head while tracing, otherwise at the snapped
            // cursor. The slider head snaps exactly like a placed circle - both the circle and slider tools do.
            Vector2 ghost = buildingSlider && sliderAnchors.Count > 0
                ? sliderAnchors[0].Position
                : (PlacementActive || SliderPlacementActive) && PlacementSnap != null ? PlacementSnap(pos) : pos;

            placementPreview.Size = new Vector2(currentDiameter);
            placementPreview.BorderThickness = currentDiameter * 0.08f;
            placementPreview.Position = ghost;
            placementPreview.Alpha = 0.85f;
            placementFill.Colour = colour;
            placementNumber.Text = number.ToString();
            placementNumber.Font = FontUsage.Default.With(size: currentDiameter * 0.5f, weight: "Bold");
            // Counter-flip the ghost's number so it stays upright while HardRock flips the play area.
            placementNumber.Scale = new Vector2(1, modHardRock ? -1 : 1);

            // Live follow points for the pending object. While a slider body is being traced this is driven by
            // updateSliderTrace (which knows the tail position/time); otherwise the pending object is a single
            // point (a circle, or a slider head not yet committed), so it connects like a circle.
            if (!buildingSlider)
            {
                double startTime = snappedNow();
                updatePreviewFollowPoints(ghost, ghost, startTime, startTime, number == 1);
            }

            if (SliderPlacementActive)
                updateSliderTrace(pos);
        }

        /// <summary>Redraws the live slider preview (the finished body + tail) through the committed anchors plus the current cursor.</summary>
        private void updateSliderTrace(Vector2 cursor)
        {
            if (sliderPreview == null || !buildingSlider)
                return;

            var pts = new List<SliderControlPoint>(sliderAnchors) { new SliderControlPoint(clampToPlayfield(cursor)) };
            updateSliderNodes(pts);

            // Render exactly the slider that PlaceSlider will commit: same type-inference + tick snap, so the
            // preview shows the finalized (trimmed) body rather than the raw drawn polyline.
            var path = actions.PlacementSliderPath(pts);

            if (path.Count < 2)
            {
                sliderPreview.Hide();
                if (sliderTailPreview != null) sliderTailPreview.Alpha = 0;
                clearPreviewFollowPoints();
                actions.ClearSliderPreview();
                return;
            }

            sliderPreview.Show();
            sliderPreview.Vertices = path;
            sliderPreview.Position = -sliderPreview.PositionInBoundingBox(Vector2.Zero);

            if (sliderTailPreview != null)
            {
                sliderTailPreview.Alpha = 0.85f;
                sliderTailPreview.Position = path[^1];
            }

            // Live follow points: previous object -> slider head, and slider tail -> next object. The start time
            // is the locked-in build time (not the live playhead), so scrubbing doesn't shift them.
            double startTime = sliderBuildStartTime;
            double duration = actions.PlacementSliderDuration(pts);
            updatePreviewFollowPoints(sliderAnchors[0].Position, path[^1], startTime, startTime + duration, previewCombo().number == 1);

            // Show, in real time, how long the slider will occupy on the top timeline.
            actions.PreviewSliderPlacement(pts);
        }

        /// <summary>A translucent combo-coloured ring matching the head placement ghost, used for the slider tail preview.</summary>
        private CircularContainer ghostRing(Color4 colour) => new CircularContainer
        {
            Origin = Anchor.Centre,
            Size = new Vector2(currentDiameter),
            Masking = true,
            BorderThickness = currentDiameter * 0.08f,
            BorderColour = Color4.White,
            Alpha = 0,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
                Alpha = 0.35f,
            },
        };

        /// <summary>Redraws the control polygon (connection lines) plus a small handle on each committed anchor (and the cursor) of the slider being drawn.</summary>
        private void updateSliderNodes(IReadOnlyList<SliderControlPoint> pts)
        {
            if (sliderNodes == null)
                return;

            sliderNodes.Clear();

            // The thin control-polygon lines connecting consecutive anchors, drawn first so the handles sit on
            // top (mirrors the post-placement SliderControlPointVisualiser polygon).
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1].Position;
                Vector2 d = pts[i].Position - a;
                sliderNodes.Add(new Box
                {
                    Position = a,
                    Origin = Anchor.CentreLeft,
                    Width = d.Length,
                    Height = 1.5f,
                    Rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X)),
                    Colour = new Color4(1f, 1f, 1f, 0.35f),
                });
            }

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

        /// <summary>The beat-snapped current time (the time a placed object would receive).</summary>
        private double snappedNow() => SnappedTimeSource?.Invoke() ?? TimeSource?.Invoke() ?? 0;

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
