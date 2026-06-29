using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Utils;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Framework.Timing;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Beatmaps.Difficulty;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using OsuBeatmapEditor.Resources;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The map editor: audio-driven play area with the difficulty's hit objects, a bottom timeline,
    /// editor settings + song settings dialogs, and unsaved-changes handling on exit.
    /// </summary>
    public partial class EditorScreen : Screen, IEditorActions
    {
        private const float top_bar_height = TopTimeline.HEIGHT;
        private const float bottom_bar_height = EditorTimeline.HEIGHT;

        // Top/bottom inset the playfield keeps when the HUD is hidden - small breathing room so it recentres and
        // grows slightly into the space the timelines used to occupy, without going fully edge-to-edge.
        private const float hidden_bar_inset = 28f;

        // Width reserved on the right of the top timeline for the adjacent settings panel (beat divisor / BPM / SV).
        private const float timeline_side_width = 168f;

        // Height of each hitsound lane row when the editor is expanded; the three lanes (plus the object band)
        // give the timeline its extra height. Kept compact so the playfield stays large.
        private const float hitsound_lane_height = 44f;

        private readonly BeatmapSetModel set;
        private readonly BeatmapDifficultyModel difficulty;

        // Cached by the game host; absent under the standalone test browser, so calls are null-guarded.
        [Resolved(CanBeNull = true)]
        private ToastOverlay? toasts { get; set; }

        // OS clipboard, for writing the modding timestamp on copy (cached by the host; null-guarded for tests).
        [Resolved(CanBeNull = true)]
        private Clipboard? hostClipboard { get; set; }

        // App-wide usage statistics (cached by the game root; absent under the test browser).
        [Resolved(CanBeNull = true)]
        private Statistics.StatisticsTracker? statistics { get; set; }

        /// <summary>Stable per-map key for the statistics tracker (independent of the .osu hash).</summary>
        private string statisticsKey => Statistics.StatisticsTracker.MapKey(set.Artist, set.Title, set.Author, difficulty.DifficultyName);

        // Online login session + local collab links (both optional; absent under the test browser / when logged out).
        [Resolved(CanBeNull = true)]
        private Online.AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private Online.CollabSession? collabs { get; set; }

        // Local cache of the user's saved patterns; lets a "save pattern" land instantly before the upload finishes.
        [Resolved(CanBeNull = true)]
        private Online.PatternStore? patternStore { get; set; }

        // The framework's global config; used to apply the power-saving frame cap. Absent under the test browser.
        [Resolved(CanBeNull = true)]
        private osu.Framework.Configuration.FrameworkConfigManager? frameworkConfig { get; set; }

        private EditorSettings settings = null!;
        private EditableBeatmap editable = null!;
        private readonly EditorSelection selection = new EditorSelection();
        private readonly NodeSelection nodeSelection = new NodeSelection();
        private readonly BeatSnapDivisor beatDivisor = new BeatSnapDivisor();
        private readonly Dictionary<int, HitObjectModel> moveSnapshot = new Dictionary<int, HitObjectModel>();

        // Slider repeat (reverse) drag state: the slider being reshaped, the pre-drag map snapshot for undo,
        // and whether the count actually changed during the drag.
        private int repeatDragId = -1;
        private HitObjectModel repeatDragOriginal;
        private Snapshot? repeatDragSnapshot;
        private bool repeatDragChanged;

        // Slider length / velocity drag (dragging a slider's tail end-cap on the playfield, like osu!lazer's
        // EndDragMarker). State captured at Begin and reused by the preview/commit calls so the live drag can
        // recompute without mutating the map (which would recreate the control-point visualiser mid-drag).
        private int lengthDragId = -1;
        private HitObjectModel lengthDragOriginal;
        private Snapshot? lengthDragSnapshot;
        private bool lengthDragChanged;
        private List<Vector2> lengthDragFullPath = new List<Vector2>();
        private double lengthDragCalculatedDistance;
        private double lengthDragOldExpected;
        private double lengthDragOldVelocity;     // effective green-line SV at the slider's start when the drag began
        private double lengthDragOldSpanDuration; // ms for one head-to-tail span at the start, kept fixed in velocity mode
        private double lengthDragResetSv;         // SV in force going into the slider, restored in a reset line at its tail
        private int lengthDragOldEnd;             // the slider's tail time at drag start (to relocate a stale reset line)
        private Vector2 lengthDragGrabOffset;
        private bool lengthDragVelocityMode;
        private double lengthDragPendingExpected;
        private double lengthDragPendingDuration;
        private double lengthDragPendingSv;

        // Timeline velocity drag (Shift + dragging a slider's tail on the top timeline): changes its speed by
        // changing its duration while keeping the path length, instead of adding reverses.
        private int velocityDragId = -1;
        private HitObjectModel velocityDragOriginal;
        private Snapshot? velocityDragSnapshot;
        private bool velocityDragChanged;
        private double velocityDragPixelLength;
        private double velocityDragResetSv;
        private int velocityDragOldEnd;
        private double velocityDragPendingSv;
        private double velocityDragPendingDuration;

        // Spinner duration drag (dragging a spinner's tail on the top timeline to change its end time).
        private int spinnerDragId = -1;
        private HitObjectModel spinnerDragOriginal;
        private Snapshot? spinnerDragSnapshot;
        private bool spinnerDragChanged;

        // Selection-transform (rotate/scale/flip) state: the pre-gesture object snapshot, the surrounding
        // quad it had, the pre-gesture map snapshot for undo, and whether the gesture changed anything.
        private Dictionary<int, HitObjectModel>? transformSnapshot;
        private RectangleF transformQuad;
        private Snapshot? transformUndo;
        private bool transformChanged;
        private int moveTimeDelta;
        private Vector2 movePosDelta;
        private Vector2 moveMin, moveMax;

        // Distance snapping: when on, placement is spaced from the previous object proportionally to the time
        // gap (so streams keep slider velocity). Spacing multiplier is adjusted with Alt+scroll, like lazer.
        private bool distanceSnapEnabled;
        private double distanceSpacing = 1.0;

        /// <summary>An undo snapshot of the editable map state (objects + timing points).</summary>
        private sealed record Snapshot(List<HitObjectModel> Objects, List<TimingPointModel> TimingPoints, List<Annotations.Annotation> Annotations);

        private readonly Stack<Snapshot> undoStack = new Stack<Snapshot>();
        private readonly Stack<Snapshot> redoStack = new Stack<Snapshot>();
        private ParsedBeatmap parsed = new ParsedBeatmap();

        private Playfield playfield = null!;
        private Container composeContainer = null!;
        private TopTimeline topTimeline = null!;

        // Toggles the expanded hitsound-lanes editor in the top timeline (the playfield shrinks to make room).
        private readonly BindableBool hitsoundMode = new BindableBool();

        // Mod-preview toggles: visualise the map as it would play under HardRock (flipped + harder AR/CS) and
        // Hidden (objects fade out, no approach circles). Purely visual - they never touch the saved map.
        private readonly BindableBool hardRockMod = new BindableBool();
        private readonly BindableBool hiddenMod = new BindableBool();
        // Easy (bigger circles + slower approach: CS/AR halved). HardRock and Easy are mutually exclusive (osu!'s
        // rule); Hidden and the rate mods mix with anything.
        private readonly BindableBool easyMod = new BindableBool();
        // Rate mod, cycled by the DT/NC chip: off → DoubleTime (1.5x, pitch preserved) → Nightcore (1.5x, pitch
        // raised) → off. Both speed up the actual playback (audio + playhead + approach + hitsounds all follow the
        // clock); DT via a Tempo adjustment, NC via Frequency (which also shifts the pitch).
        private readonly Bindable<RateMod> rateMod = new Bindable<RateMod>(RateMod.Off);
        private const double rate_mod_speed = 1.5;
        private readonly BindableNumber<double> dtTempoRate = new BindableDouble(1);
        private readonly BindableNumber<double> ncFrequencyRate = new BindableDouble(1);
        // Auto-mod preview: a cursor that plays the map. Colour + trail length tweakable in a popover under the chip.
        private readonly BindableBool autoMod = new BindableBool();
        private readonly Bindable<Color4> autoColour = new Bindable<Color4>(new Color4(1f, 0.86f, 0.2f, 1f));
        private readonly BindableInt autoTrail = new BindableInt(10) { MinValue = 0, MaxValue = 120 };
        private readonly BindableFloat autoTrailWidth = new BindableFloat(1f) { MinValue = 0.2f, MaxValue = 4f };
        // Auto key overlay: show the K1/K2 "tapping" indicator alongside the auto cursor (persisted).
        private readonly BindableBool autoKeyOverlay = new BindableBool();
        // Humanise the auto cursor: arcs, overshoot, jitter and aim error instead of perfect Auto (persisted).
        private readonly BindableBool autoHumanize = new BindableBool();
        private AutoPreviewMenu autoMenu = null!;
        private MenuDotsButton autoMenuButton = null!;
        private bool autoMenuOpen;
        private HumanizeTuningPanel humanizeTuningPanel = null!;
        // Modding Mode: review the beatmap's osu! discussion ("mod") entries. Discussion bubbles appear on the
        // top timeline; filters sit on the left, messages on the right; HD/HR are forced off (Auto-only).
        private readonly BindableBool moddingMode = new BindableBool();

        // Review mode: the editor-only, shareable modding-annotation layer (notes/lines, persisted per difficulty
        // and exportable as a .sobemod file). State + the in-memory document + its local store.
        private readonly BindableBool reviewMode = new BindableBool();
        private Annotations.AnnotationDocument reviewDoc = new Annotations.AnnotationDocument();
        private Annotations.AnnotationStore reviewStore = null!;
        private ReviewToolbar reviewToolbar = null!;
        private ReviewToolPanel reviewToolPanel = null!;
        private NoteEditPopover noteEditPopover = null!;
        private Container patternPreviewBox = null!;
        private Container patternPreviewContent = null!;
        private FillFlowContainer reviewLeftPanel = null!;
        private ReviewTool reviewTool = ReviewTool.Select;
        // Default visible span of a freshly-drawn stroke (ms); the modder retimes it on the top timeline.
        private const double default_stroke_duration_ms = 1000;
        // A note being created isn't added to the document until its text is committed, so creation is one atomic
        // (undoable) step and cancelling leaves nothing behind.
        private Annotations.Annotation? pendingNewNote;
        private Action<string>? reviewDragDropHandler;
        private ModToggleButton hiddenButton = null!;
        private ModToggleButton hardRockButton = null!;
        private ModToggleButton easyButton = null!;
        private RateModButton rateModButton = null!;
        private Container moddingRegion = null!;
        private ModdingPanel moddingPanel = null!;
        // Width reserved on the right for the modding panel; the playfield eases left to make room.
        private const float modding_panel_width = 320f;
        private const float modding_panel_gap = 12f;
        private float moddingInsetCurrent;
        private List<Online.ModdingDiscussion>? loadedDiscussions;
        private bool discussionsRequested;
        // Smoothly-interpolated current top-bar height, lerped toward its collapsed/expanded target each frame.
        private float topHeightCurrent = top_bar_height;
        private float bottomHeightCurrent = bottom_bar_height;

        // When true the editor chrome (timelines, panels, chips, counters) is hidden so only the background/grid +
        // hit objects show; the playfield recentres and grows slightly. Toggled with Shift+Tab or the eye button.
        private bool hudHidden;
        private Container hudLayer = null!;
        private SpriteText bpmText = null!;
        private SpriteText svText = null!;
        private SpriteText distanceSnapText = null!;

        // Live star-rating chip (same pill design as the carousel) + its debounced recompute state.
        private Container starChip = null!;
        private double currentStars;
        private int starComputeGeneration;
        private ScheduledDelegate? starRecomputeDebounce;
        private BeatDivisorControl beatDivisorControl = null!;
        private Container timelineSidePanel = null!;
        private Container timelineLeftPanel = null!;
        private FillFlowContainer modButtons = null!;
        private FillFlowContainer leftPanels = null!;
        private HitsoundBankBar hitsoundBankBar = null!;
        private Container hitsoundControlsBlock = null!; // left-side block (below the toolbar) hosting the bank bar in lanes mode
        private double lastDistanceSpacing = double.NaN;
        private ToolPanel toolPanel = null!;
        private FillFlowContainer toolButtons = null!;
        private EditorSettingsOverlay settingsOverlay = null!;
        private SongSettingsOverlay songSettingsOverlay = null!;
        private BetaNoticeOverlay betaOverlay = null!;
        private TimingPointsOverlay timingPointsOverlay = null!;
        private EditorTimeline bottomTimeline = null!;
        private ConfirmExitOverlay confirmExit = null!;
        private RotationPopover rotationPopover = null!;
        private ExportMenu exportMenu = null!;
        private UI.IconBarButton exportButton = null!;
        private TimingPillPopover timingPillPopover = null!;
        private PlaybackControl playbackControl = null!;

        private CollabOverlay collabOverlay = null!;
        private UI.SliderToStreamOverlay sliderToStreamOverlay = null!;
        private UI.PatternGalleryOverlay patternGallery = null!;
        private SavePatternButton savePatternButton = null!;

        /// <summary>True while the open difficulty is linked to a server collab; lights the COLLAB chip.</summary>
        private readonly BindableBool collabLinked = new BindableBool();

        /// <summary>True while authorship-colour mode is on (objects tinted by who placed them).</summary>
        private readonly BindableBool authorshipOn = new BindableBool();
        private AuthorsButton authorsButton = null!;
        private FillFlowContainer authorLegend = null!;
        private Online.CollabAuthorship? authorship;
        private readonly System.Collections.Generic.Dictionary<long, Color4> authorColours = new System.Collections.Generic.Dictionary<long, Color4>();
        private bool authorshipBusy;

        // Distinct palette for authorship colouring, assigned per author in contribution order.
        private static readonly Color4[] author_palette =
        {
            new Color4(0.36f, 0.72f, 1f, 1f),    // blue
            new Color4(1f, 0.46f, 0.46f, 1f),    // red
            new Color4(0.55f, 0.86f, 0.40f, 1f), // green
            new Color4(1f, 0.78f, 0.30f, 1f),    // amber
            new Color4(0.78f, 0.55f, 1f, 1f),    // purple
            new Color4(0.40f, 0.86f, 0.80f, 1f), // teal
            new Color4(1f, 0.60f, 0.85f, 1f),    // pink
            new Color4(0.85f, 0.85f, 0.55f, 1f), // olive
        };

        private GameHost host = null!;
        private ITrackStore? trackStore;
        private Track? track;
        private InterpolatingFramedClock? audioClock;

        // Visuals (playhead, objects, timelines) read this offset clock instead of the raw audio clock: while
        // playing it shifts them by the user's audio offset so they line up with delayed output (e.g. Bluetooth).
        // Authoring (CurrentTime) and hitsound feedback stay on the raw audioClock.
        private FramedOffsetClock? visualClock;
        private HitsoundPlayer? hitsounds;
        private int hitsoundIndex;
        private double lastHitsoundTime;
        private bool needHitsoundResync = true;

        /// <summary>What a scheduled <see cref="SampleEvent"/> plays.</summary>
        private enum SampleEventKind { Hit, SliderTick }

        /// <summary>A single scheduled hitsound playback: object heads, each slider node, spinner ends, and slider ticks.</summary>
        private readonly record struct SampleEvent(double Time, SampleEventKind Kind, int HitSound, SampleBank Normal, SampleBank Addition, float Volume, int Index = 0, string Filename = "");

        /// <summary>All hitsound events for the map, time-sorted; rebuilt after every edit (see <see cref="rebuildSampleEvents"/>).</summary>
        private readonly List<SampleEvent> sampleEvents = new List<SampleEvent>();

        // Continuous body loops (sliderslide/sliderwhistle/spinnerspin): the channels currently sounding, keyed by
        // "{objectId}:{role}", plus per-frame scratch sets so the active loops are recomputed from the playhead each frame.
        private readonly Dictionary<string, SampleChannel> activeLoops = new Dictionary<string, SampleChannel>();
        private readonly HashSet<string> desiredLoops = new HashSet<string>();
        private readonly List<string> loopsToStop = new List<string>();

        /// <summary>The hitsounds applied to newly placed objects (set from the left-panel palette when nothing is selected).</summary>
        private int pendingHitSound;
        // New objects default to Auto banks (inherit the timing point), like osu!lazer.
        private SampleBank pendingNormalBank = SampleBank.Auto;
        private SampleBank pendingAdditionBank = SampleBank.Auto;
        // New objects also inherit the timing point's volume + custom sample index (0 = inherit/Auto), like osu!lazer.
        private float pendingSampleVolume; // 0 = inherit (Auto), else 0..1 explicit override
        private int pendingSampleIndex;    // 0 = inherit (Auto), else explicit custom-sample-bank index
        private LargeTextureStore? textures;

        /// <summary>The hitsounds copied by "Copy hitsounds", ready to paste onto another selection (null = nothing copied).</summary>
        private HitsoundClip? hitsoundClipboard;
        private Texture? backgroundTexture;
        private ScheduledDelegate? circleSizeRebuild;

        /// <summary>The playback position (ms) when the editor was last exited; used to resume the menu preview.</summary>
        public double LastTime { get; private set; }

        /// <summary>True if the map was saved at least once this session (so song select can refresh).</summary>
        public bool DidSave { get; private set; }

        public EditorScreen(BeatmapSetModel set, BeatmapDifficultyModel difficulty)
        {
            this.set = set;
            this.difficulty = difficulty;
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var deps = new DependencyContainer(parent);

            settings = new EditorSettings(parent.Get<GameHost>().Storage);

            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);
            if (osuPath != null)
                parsed = OsuFileDecoder.Decode(osuPath);

            editable = new EditableBeatmap(parsed, settings.DefaultCreator.Value, settings);

            deps.CacheAs(settings);
            deps.CacheAs(editable);
            deps.CacheAs(selection);
            deps.CacheAs(nodeSelection);
            deps.CacheAs(beatDivisor);
            deps.CacheAs<IEditorActions>(this);
            return deps;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio, GameHost host)
        {
            this.host = host;
            statistics?.EnterMap(statisticsKey);
            track = loadTrack(audio, host);
            backgroundTexture = loadBackgroundTexture(host);

            // Interpolate the audio clock: BASS only advances CurrentTime at the buffer rate, so reading
            // it raw each frame stair-steps the playfield. The interpolating clock fills the gaps smoothly.
            if (track != null)
            {
                audioClock = new InterpolatingFramedClock();
                audioClock.ChangeSource(track);

                // The visual clock wraps the (manually processed) audio clock; its offset is set per frame in
                // advanceClocks(). processSource:false so it doesn't re-advance the audio clock a second time.
                visualClock = new FramedOffsetClock(audioClock, processSource: false);

                // Rate-mod preview: speeding up the track also speeds up the interpolating clock, and so the
                // playhead + object approach + hitsounds. DoubleTime uses Tempo (pitch preserved); Nightcore uses
                // Frequency (pitch rises). Both driven by the DT/NC chip via these adjustments.
                track.AddAdjustment(AdjustableProperty.Tempo, dtTempoRate);
                track.AddAdjustment(AdjustableProperty.Frequency, ncFrequencyRate);
            }

            // Hitsound feedback: bundled default-skin samples, played as playback crosses each object.
            var skinSampleStore = audio.GetSampleStore(
                new NamespacedResourceStore<byte[]>(new DllResourceStore(OsuBeatmapEditorResources.ResourceAssembly), "Samples"));

            // The map's OWN packed samples (custom hitsounds) are consulted first, like osu!lazer's beatmap skin.
            ISampleStore? beatmapSampleStore = !string.IsNullOrEmpty(set.DataDirectory) && set.Files.Count > 0
                ? audio.GetSampleStore(new BeatmapSampleStore(set.Files, set.DataDirectory))
                : null;

            hitsounds = new HitsoundPlayer(skinSampleStore, beatmapSampleStore);
            rebuildSampleEvents();

            InternalChildren = new Drawable[]
            {
                buildBackground(host),
                composeContainer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = top_bar_height, Bottom = bottom_bar_height },
                    Child = playfield = new Playfield(),
                },
                // Everything below is editor "chrome" (timelines, panels, chips, counters): grouped so the whole
                // HUD can be hidden in one go (Shift+Tab), leaving only the background/grid + hit objects visible.
                hudLayer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                // Authorship legend (who-placed-what colours); shown only while authorship mode is on.
                authorLegend = new FillFlowContainer
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = timeline_side_width + 12, Bottom = bottom_bar_height + 12 },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Alpha = 0,
                },
                // The top bar is a fixed block: a left panel (tool buttons), the timeline, and the settings panel
                // (divisor/BPM/SV) on the right. The timeline is inset by the SAME width on both sides so its centre
                // playhead (the pink line) stays at the true screen centre, and so it sits flush against both panels.
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Padding = new MarginPadding { Left = timeline_side_width, Right = timeline_side_width },
                    Child = topTimeline = new TopTimeline(parsed, () => VisualTime, track?.Length ?? 0, hitsoundMode)
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        TimingPillClicked = onTimingPillClicked,
                        // Lets the hitsound cells dim by their effective volume (object override, else the timing point).
                        TimingVolume = volumeAt,
                    },
                },
                // Left block of the top bar: tool buttons (Song Setup / Settings) plus the Modding / Hitsounds
                // toggles. Mirrors the settings panel's width so the timeline stays centred. (Room here for more.)
                timelineLeftPanel = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Width = timeline_side_width,
                    Height = top_bar_height,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.82f },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 6),
                            Children = new Drawable[]
                            {
                                toolButtons = buildToolButtons(),
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(6, 0),
                                    Children = new Drawable[]
                                    {
                                        new HitsoundModeButton(hitsoundMode),
                                        new CollabButton(collabLinked, () => collabOverlay.ToggleVisibility(), "Collab - co-map this difficulty with someone (\"git for maps\")"),
                                        new UI.IconBarButton(osu.Framework.Graphics.Sprites.FontAwesome.Solid.EyeSlash, "Hide interface (Shift+Tab) - show only the grid/background + objects",
                                            toggleHud),
                                        authorsButton = new AuthorsButton(authorshipOn, toggleAuthorship, "Authors - colour objects by who placed them") { Alpha = 0 },
                                    },
                                },
                            },
                        },
                    },
                },
                // The hitsound controls (banks/volume/index/copy-paste) as a left-side block directly below the
                // top-left toolbar column, shown only while the hitsound lanes are open. Keeps the controls OUT of the
                // timeline so the lanes keep (almost) the full timeline width. Y is fixed; Height tracks the lanes band.
                hitsoundControlsBlock = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Width = timeline_side_width,
                    Y = top_bar_height,
                    Masking = true,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.82f },
                        hitsoundBankBar = new HitsoundBankBar
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            StateProvider = CurrentHitsoundState,
                            SetNormalBank = SetNormalBank,
                            SetAdditionBank = SetAdditionBank,
                            SetVolume = SetSampleVolume,
                            SetIndex = SetSampleIndex,
                            CopyHitsounds = CopyHitsounds,
                            PasteHitsounds = PasteHitsounds,
                            HasClip = () => HasHitsoundClip,
                        },
                    },
                },
                // Compact icon row in the bottom-left (right of the background-dim wheel): Patterns gallery + the
                // Modding / Review mode toggles. Icons (not wide text chips) so they stay clear of the playfield.
                new FillFlowContainer
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = 50, Bottom = bottom_bar_height + 12 },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new IconBarButton(FontAwesome.Solid.Shapes, $"Pattern gallery ({Shortcut.CommandName}+Shift+P)", () => patternGallery.ToggleVisibility())
                        {
                            Size = new Vector2(30),
                        },
                        new IconToggleButton(moddingMode, FontAwesome.Solid.Comments, $"Modding mode ({Shortcut.CommandName}+Shift+M) - review the map's osu! discussions"),
                        new IconToggleButton(reviewMode, FontAwesome.Regular.StickyNote, $"Review mode ({Shortcut.CommandName}+Shift+A) - draw notes/annotations only visible in this editor, shareable as a .sobemod file"),
                    },
                },
                bottomTimeline = new EditorTimeline(track, parsed, () => VisualTime, rightInset: PlaybackControl.WIDTH) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
                playbackControl = new PlaybackControl(track, () => track?.IsRunning ?? false, togglePlay)
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                },
                new BackgroundToggleButton(settings.BackgroundDim, backgroundTexture != null)
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = 12, Bottom = bottom_bar_height + 12 },
                },
                new FpsCounter
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Margin = new MarginPadding { Right = 12, Bottom = bottom_bar_height + 12 },
                },
                // Per-map editing-time readout, stacked just above the FPS counter.
                new MapStatsDisplay
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Margin = new MarginPadding { Right = 12, Bottom = bottom_bar_height + 12 + 26 },
                },
                // The settings panel adjacent to (the right of) the top timeline: beat divisor on top, then the
                // BPM and SV readouts stacked below it. Sits in the column the timeline padding reserves.
                timelineSidePanel = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Width = timeline_side_width,
                    Height = top_bar_height,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.82f },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 3),
                            Children = new Drawable[]
                            {
                                beatDivisorControl = new BeatDivisorControl
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                },
                                bpmText = new SpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Label(numeric: true),
                                },
                                // SV readout + live star-rating pill share one row: stacking all four readouts
                                // overflowed the fixed-height panel and clipped the star at the bottom. The star
                                // uses the same pill design as the song-select carousel.
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(8, 0),
                                    Margin = new MarginPadding { Top = 2 },
                                    Children = new Drawable[]
                                    {
                                        svText = new SpriteText
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Colour = EditorTheme.Colours.Velocity,
                                            Font = EditorTheme.Type.Label(numeric: true),
                                        },
                                        starChip = new Container
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            AutoSizeAxes = Axes.Both,
                                            Child = new StarRatingDisplay(0),
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
                distanceSnapText = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 96 },
                    Colour = EditorTheme.Colours.Selection,
                    Font = EditorTheme.Type.Label(),
                    Alpha = 0,
                },
                // Mod-preview chips, just below the settings panel. A wrapping grid (constrained to the panel's
                // width) so the row of chips doesn't span far across the screen - they stack into 2 rows instead.
                modButtons = new FillFlowContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 96 },
                    // Exactly three chips wide (3·38 + 2·6) so the grid wraps to 2 rows and stays flush-right.
                    Width = 3 * 38 + 2 * 6,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(6, 6),
                    Children = new Drawable[]
                    {
                        easyButton = new ModToggleButton(easyMod, "EZ", EditorTheme.Colours.Velocity, "Easy - bigger circles, slower approach (CS/AR halved). Can't mix with HardRock"),
                        hiddenButton = new ModToggleButton(hiddenMod, "HD", EditorTheme.Colours.Selection, "Hidden - objects fade out, no approach circles"),
                        hardRockButton = new ModToggleButton(hardRockMod, "HR", EditorTheme.Colours.Error, "HardRock - flipped vertically, harder AR/CS. Can't mix with Easy"),
                        rateModButton = new RateModButton(rateMod, OsuColour.Purple, EditorTheme.Colours.Accent, "Speed up 1.5x - click for DoubleTime (same pitch), again for Nightcore (higher pitch), again to turn off"),
                        new ModToggleButton(autoMod, "AU", EditorTheme.Colours.Info, "Auto - a cursor plays the map (right-click for colour/trail)"),
                        autoMenuButton = new MenuDotsButton(toggleAutoMenu) { Alpha = 0 },
                    },
                },
                // The Auto-preview popover (cursor colour + trail length), opened on demand from the "..." button.
                autoMenu = new AutoPreviewMenu(autoColour, autoTrail, autoTrailWidth, autoKeyOverlay, autoHumanize, toggleHumanizeTuning)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Alpha = 0,
                },
                // Live tuning panel for the Humanize model; opened from the AU mini-menu, edits HumanizeTuning in real time.
                humanizeTuningPanel = new HumanizeTuningPanel(() => humanizeTuningPanel!.Hide(), () => settings.SaveHumanizeTuning())
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Alpha = 0,
                },
                // Modding Mode: a panel docked to the right of the compose area (filters + messages). The
                // playfield eases left to make room (driven in updateHitsoundLayout). Slides in/out from the right.
                moddingRegion = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = top_bar_height, Bottom = bottom_bar_height },
                    Child = moddingPanel = new ModdingPanel(settings.ModdingMutedTypes)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = modding_panel_width,
                        Margin = new MarginPadding { Vertical = EditorTheme.Spacing.Md },
                        X = modding_panel_width, // start fully off the right edge
                    },
                },
                // Review mode: tool box + identity/export controls, docked on the LEFT (where the compose tools sit),
                // shown only while reviewing so it doesn't cover the playfield.
                reviewLeftPanel = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 12 },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        reviewToolPanel = new ReviewToolPanel { ToolSelected = applyReviewTool },
                        reviewToolbar = new ReviewToolbar(settings.ReviewAuthorName, settings.ReviewAuthorColour, settings.ReviewShowAlways, loggedInModderName())
                        {
                            OnExport = exportReviewLayer,
                            OnImport = importReviewLayerPrompt,
                        },
                    },
                },
                leftPanels = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 12 },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                    Children = new Drawable[]
                    {
                        toolPanel = new ToolPanel { ToolSelected = applyTool },
                        // Appears only while holding Shift over a selection: a quick "save this selection as a pattern".
                        savePatternButton = new SavePatternButton
                        {
                            ShouldShow = () => selection.Selected.Count > 0,
                            OnSave = saveSelectionAsPattern,
                        },
                    },
                },
                    },
                },
                settingsOverlay = new EditorSettingsOverlay(),
                songSettingsOverlay = new SongSettingsOverlay(),
                timingPointsOverlay = new TimingPointsOverlay(parsed, () => CurrentTime),
                confirmExit = new ConfirmExitOverlay
                {
                    OnSave = () => { save(); this.Exit(); },
                    OnDiscard = this.Exit,
                },
                rotationPopover = new RotationPopover
                {
                    OnRotate = (degrees, aroundPlayfield) => RotateSelectionBy(degrees, aroundPlayfield),
                },
                noteEditPopover = new NoteEditPopover
                {
                    OnSaved = onNoteSaved,
                    OnDeleted = onNoteDeleted,
                },
                // Hover-preview for inline timestamps in note text: a small black box showing the referenced pattern.
                patternPreviewBox = new Container
                {
                    Size = new Vector2(172, 132),
                    Origin = Anchor.BottomCentre,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Md,
                    BorderThickness = 1.5f,
                    BorderColour = EditorTheme.Colours.Accent,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black },
                        patternPreviewContent = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(8) },
                    },
                },
                exportMenu = new ExportMenu
                {
                    OnExportOsz = exportMap,
                    OnExportOsu = exportDifficultyOsu,
                },
                timingPillPopover = new TimingPillPopover
                {
                    OnApply = UpdateTimingPoint,
                    OnDelete = DeleteTimingPoint,
                },
                collabOverlay = new CollabOverlay
                {
                    IsLoggedIn = () => auth?.IsLoggedIn == true,
                    CurrentLink = () => collabs?.Get(statisticsKey),
                    StartCollab = startCollabAsync,
                    AddMember = addCollabMemberAsync,
                    PushProgress = pushCollabProgressAsync,
                },
                sliderToStreamOverlay = new UI.SliderToStreamOverlay
                {
                    Preview = refreshStreamPreview,
                    Confirmed = confirmStreamPreview,
                    Cancelled = cancelStreamPreview,
                },
                patternGallery = new UI.PatternGalleryOverlay
                {
                    AddToMap = d => pasteObjects(d.Objects, d.SourceVelocities, d.SourceBeatLength),
                },
                // Frontmost so the beta notice sits above all editor chrome when shown on open.
                betaOverlay = new BetaNoticeOverlay(),
            };

            // Visibility/fade follow the (offset) visual time so objects scroll in sync with the playhead; placement
            // snapping stays on the raw authoring time so the audio offset never shifts where objects land.
            playfield.TimeSource = () => VisualTime;
            playfield.SnappedTimeSource = () => snapTime(CurrentTime);
            playfield.PlacementSnap = SnapPlacement;
            playfield.SliderTickDistance = sliderTickDistance;

            // The Authors chip only makes sense for a collab; show it when linked, and drop authorship mode if
            // the link goes away.
            collabLinked.BindValueChanged(v =>
            {
                authorsButton.Alpha = v.NewValue ? 1 : 0;
                if (!v.NewValue && authorshipOn.Value)
                    toggleAuthorship();
            }, true);

            // Hide the left tool/hitsound column while the lanes editor is open (it owns hitsound editing now),
            // and show the lazer-style bank bar instead. Modding Mode also hides this left chrome, so the
            // visibility is computed from both flags (see updateLeftChrome) rather than written directly here -
            // otherwise toggling hitsounds inside Modding Mode would bring the tools back.
            hitsoundMode.BindValueChanged(_ => updateLeftChrome(), true);

            // A node selection only makes sense while its slider is selected; drop it otherwise.
            selection.Changed += () =>
            {
                if (nodeSelection.Selected is { } n && !selection.Contains(n.ObjectId))
                    nodeSelection.Clear();
            };

            // Live difficulty: AR changes the approach window (and the stack window); CS rebuilds (debounced).
            editable.Ar.BindValueChanged(v =>
            {
                applyStacking();
                rebuildHitObjects();
            }, true);

            // Hit objects animate against the offset visual clock (set in load) so their approach/fade transforms
            // track the same shifted playhead the timelines use; falls back to the raw audio clock if unavailable.
            if (visualClock != null)
                playfield.SetClock(visualClock);
            else if (audioClock != null)
                playfield.SetClock(audioClock);
            editable.Cs.BindValueChanged(_ =>
            {
                circleSizeRebuild?.Cancel();
                circleSizeRebuild = Scheduler.AddDelayed(rebuildHitObjects, 80);
            });
            editable.StackLeniency.BindValueChanged(v =>
            {
                parsed.StackLeniency = v.NewValue;
                applyStacking();
                rebuildHitObjects();
            });
            // Changing the base slider velocity re-times every existing slider (its pixel length is unchanged).
            editable.SliderMultiplier.BindValueChanged(v =>
            {
                parsed.SliderMultiplier = v.NewValue;
                recomputeSliderDurations();
            });
            editable.SliderTickRate.BindValueChanged(v =>
            {
                parsed.SliderTickRate = v.NewValue;
                // Tick rate isn't part of the per-object model, so force a redraw to refresh the tick dots.
                playfield.RebuildObjects();
            });

            // The difficulty settings that move the star rating: recompute the live SR chip (debounced). Object
            // and timing edits are already covered via afterEdit(). AR doesn't affect the no-mod SR.
            editable.Cs.BindValueChanged(_ => scheduleStarRecompute());
            editable.Od.BindValueChanged(_ => scheduleStarRecompute());
            editable.SliderMultiplier.BindValueChanged(_ => scheduleStarRecompute());
            editable.SliderTickRate.BindValueChanged(_ => scheduleStarRecompute());

            // Seed the chip with the map's initial rating.
            recomputeStars();

            // Toggling a mod re-renders the playfield: HardRock/Easy change CS (diameter) + AR (preempt) (and HR
            // flips the field vertically); DoubleTime shortens the approach; Hidden changes the per-object fade.
            // All are pure visualisation. HardRock and Easy are mutually exclusive (osu!'s rule) - turning one on
            // forces the other off; setting a mod off never cascades, so there's no feedback loop.
            hardRockMod.BindValueChanged(v =>
            {
                if (v.NewValue)
                    easyMod.Value = false;
                onModsChanged();
            });
            easyMod.BindValueChanged(v =>
            {
                if (v.NewValue)
                    hardRockMod.Value = false;
                onModsChanged();
            });
            // DT/NC just change the playback rate - the faster clock speeds up the approach on its own, so no
            // rebuild and no preempt tweak (that would double-apply the speed-up). DT keeps pitch (Tempo), NC
            // raises it (Frequency); only one of the two adjustments is non-1 at a time.
            rateMod.BindValueChanged(v =>
            {
                dtTempoRate.Value = v.NewValue == RateMod.DoubleTime ? rate_mod_speed : 1.0;
                ncFrequencyRate.Value = v.NewValue == RateMod.Nightcore ? rate_mod_speed : 1.0;
            });
            hiddenMod.BindValueChanged(_ => onModsChanged(), true);

            // Auto preview: the toggle shows the playing cursor and reveals the small "..." button to open the
            // colour/trail popover (no longer auto-opened). The colour/trail bindables feed the cursor and are
            // persisted (see below). All purely visual.
            autoMod.BindValueChanged(v =>
            {
                playfield.SetAutoPlay(v.NewValue);
                autoMenuButton.FadeTo(v.NewValue ? 1 : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                if (!v.NewValue)
                    setAutoMenuOpen(false);
            }, true);
            autoColour.BindValueChanged(c => playfield.SetAutoColour(c.NewValue), true);
            autoTrail.BindValueChanged(t => playfield.SetAutoTrailLength(t.NewValue), true);
            autoTrailWidth.BindValueChanged(w => playfield.SetAutoTrailWidth(w.NewValue), true);
            autoKeyOverlay.BindValueChanged(k => playfield.SetKeyOverlay(k.NewValue), true);
            autoHumanize.BindValueChanged(h => playfield.SetHumanize(h.NewValue), true);

            // Persist the Auto-cursor settings: seed from the stored values, then mirror edits back so they
            // survive across sessions (the colour stored as Colour4, our cursor uses osuTK Color4).
            autoColour.Value = toColor4(settings.AutoCursorColour.Value);
            autoTrail.Value = (int)settings.AutoTrailLength.Value;
            autoTrailWidth.Value = settings.AutoTrailWidth.Value;
            autoKeyOverlay.Value = settings.AutoKeyOverlay.Value;
            autoHumanize.Value = settings.AutoHumanize.Value;
            autoColour.BindValueChanged(c => settings.AutoCursorColour.Value = toColour4(c.NewValue));
            autoTrail.BindValueChanged(t => settings.AutoTrailLength.Value = t.NewValue);
            autoTrailWidth.BindValueChanged(w => settings.AutoTrailWidth.Value = w.NewValue);
            autoKeyOverlay.BindValueChanged(k => settings.AutoKeyOverlay.Value = k.NewValue);
            autoHumanize.BindValueChanged(h => settings.AutoHumanize.Value = h.NewValue);

            // Modding Mode: load the discussions the first time it's entered; toggle the side panels + bubbles;
            // force HD/HR off (Auto-only) while active.
            // No immediate invocation: the initial "off" state already matches the defaults (panels hidden,
            // HD/HR enabled), and the side panels/timeline may not be fully loaded yet at this point.
            moddingMode.BindValueChanged(v => onModdingModeChanged(v.NewValue));
            moddingPanel.OnFiltersChanged = refreshModdingViews;
            moddingPanel.OnSeek = SeekTo;
            topTimeline.ModBubbleClicked = SeekTo;
            // Selecting an object in the timeline switches back to the select tool.
            topTimeline.ObjectSelectedHere = () => applyTool(EditorTool.Selection);

            setupReviewLayer(host);
        }

        // --- Review mode (modding-annotation layer) ---

        /// <summary>Initialises the Review layer: load the saved annotations for this difficulty and wire the playfield + drag-import.</summary>
        private void setupReviewLayer(GameHost host)
        {
            reviewStore = new Annotations.AnnotationStore(host.Storage);
            reviewDoc = reviewStore.LoadLocal(reviewKey) ?? new Annotations.AnnotationDocument
            {
                OsuFileHash = difficulty.OsuFileHash,
                SetOnlineId = set.OnlineID,
                Difficulty = difficulty.DifficultyName,
                Title = set.Title,
                Artist = set.Artist,
            };

            playfield.OnReviewCreateNote = createNoteAt;
            playfield.OnReviewCreateStroke = createReviewStroke;
            playfield.ReviewLineColour = () => toColor4(settings.ReviewAuthorColour.Value);
            playfield.Annotations.NoteActivated = a => noteEditPopover.OpenFor(a, isNew: false);
            playfield.Annotations.NoteMoveStart = pushUndo;
            playfield.Annotations.NoteMoved = _ => editable.IsDirty.Value = true;
            playfield.Annotations.LineClicked = onLineClicked;
            playfield.Annotations.OnTimestampActivated = onTimestampActivated;
            playfield.Annotations.OnTextTimestampActivated = onTextTimestampActivated;
            playfield.Annotations.OnTextTimestampHover = showPatternPreview;
            playfield.Annotations.OnTextTimestampHoverLost = hidePatternPreview;
            playfield.Annotations.TimestampFormatter = formatTimestamp;
            refreshAnnotations();

            reviewMode.BindValueChanged(v => onReviewModeChanged(v.NewValue));
            // The "show always" toggle keeps notes visible on the playfield when not reviewing.
            settings.ReviewShowAlways.BindValueChanged(_ => applyAnnotationVisibility(), true);

            // One colour per modder: changing it recolours this modder's existing notes live (others' notes keep theirs).
            settings.ReviewAuthorColour.BindValueChanged(c =>
            {
                string me = reviewAuthorName();
                string hex = c.NewValue.ToHex();
                bool changed = false;
                foreach (var a in reviewDoc.Annotations)
                {
                    if (a.Author == me && a.Color != hex)
                    {
                        a.Color = hex;
                        changed = true;
                    }
                }
                if (changed)
                {
                    refreshAnnotations();
                    editable.IsDirty.Value = true;
                }
            });

            // Dropping a .sobemod file onto the editor window imports/merges it.
            if (host.Window != null)
            {
                reviewDragDropHandler = path => Schedule(() => onReviewFileDropped(path));
                host.Window.DragDrop += reviewDragDropHandler;
            }
        }

        /// <summary>A stable key for this difficulty's local Review layer (survives saves that re-hash the .osu).</summary>
        private string reviewKey => Annotations.AnnotationStore.KeyFor(set.Identity, difficulty.DifficultyName);

        /// <summary>An osu!-style timestamp for a note: <c>mm:ss:fff (1,2,3)</c>, the numbers being the referenced objects' combo numbers in time order.</summary>
        private string formatTimestamp(double time, System.Collections.Generic.List<int>? objects)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, time));
            string ts = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";

            if (objects is { Count: > 0 })
            {
                var combos = objects
                    .Select(id => parsed.HitObjects.FirstOrDefault(o => o.Id == id))
                    .Where(o => o.Id >= 0)
                    .OrderBy(o => o.StartTime)
                    .Select(o => o.ComboNumber)
                    .ToList();
                if (combos.Count > 0)
                    ts += $" ({string.Join(",", combos)})";
            }

            return ts;
        }

        /// <summary>Pushes the document's annotations to the playfield layer and the bottom-timeline markers.</summary>
        private void refreshAnnotations()
        {
            playfield.Annotations.SetAnnotations(reviewDoc.Annotations);

            var markers = new System.Collections.Generic.List<(double, Colour4, osu.Framework.Graphics.Sprites.IconUsage)>();
            var ranges = new System.Collections.Generic.List<(string, double, double, Colour4)>();
            foreach (var a in reviewDoc.Annotations)
            {
                Colour4 colour;
                try { colour = Colour4.FromHex(a.Color); } catch { colour = EditorTheme.Colours.Accent; }

                if (a.Kind == Annotations.Annotation.KindNote)
                {
                    markers.Add((a.Time, colour, ReviewIcons.For(a.Type)));
                }
                else // shape / stroke (freehand Draw)
                {
                    markers.Add((a.Time, colour, osu.Framework.Graphics.Sprites.FontAwesome.Solid.PenNib));
                    // Only show the (interactive) time-range bars while reviewing, so they don't clutter editing.
                    if (reviewMode.Value)
                        ranges.Add((a.Id, a.Time, a.EndTime ?? a.Time + default_stroke_duration_ms, colour));
                }
            }
            bottomTimeline.SetAnnotationMarkers(markers, SeekTo);
            topTimeline.SetStrokeRanges(ranges, onStrokeRangeBegin, onStrokeRangeChanged, onStrokeRangeCommit, onStrokeRangeDelete);
        }

        /// <summary>Right-clicking a stroke's range bar on the top timeline removes the stroke.</summary>
        private void onStrokeRangeDelete(string id)
        {
            var a = reviewDoc.Annotations.Find(x => x.Id == id);
            if (a != null)
                onLineClicked(a);
        }

        private void onStrokeRangeBegin() => pushUndo();

        /// <summary>Live update while dragging a stroke's range bar on the top timeline (no rebuild, so the drag stays smooth).</summary>
        private void onStrokeRangeChanged(string id, double start, double end)
        {
            var a = reviewDoc.Annotations.Find(x => x.Id == id);
            if (a == null)
                return;
            a.Time = Math.Round(Math.Max(0, start));
            a.EndTime = Math.Round(Math.Max(a.Time + 50, end));
            editable.IsDirty.Value = true;
        }

        /// <summary>Commits a stroke-range drag: resync the markers/bars from the model.</summary>
        private void onStrokeRangeCommit() => refreshAnnotations();

        private void onReviewModeChanged(bool on)
        {
            // Auto-only (same as Modding mode): mod-preview toggles off + disabled while reviewing.
            if (on)
            {
                hardRockMod.Value = false;
                hiddenMod.Value = false;
                easyMod.Value = false;
                rateMod.Value = RateMod.Off;
                applyTool(EditorTool.Selection); // drop any composing tool; clicks now drop notes
            }
            hardRockButton.SetEnabled(!on);
            hiddenButton.SetEnabled(!on);
            easyButton.SetEnabled(!on);
            rateModButton.SetEnabled(!on);

            playfield.ReviewMode = on;
            if (on)
                applyReviewTool(reviewTool); // re-assert the active review tool on entry
            // The object selection is shared between normal and Review mode (so a Select-then-review flow keeps it).
            reviewLeftPanel.FadeTo(on ? 1 : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            bottomTimeline.SetReviewMode(on);
            applyAnnotationVisibility();
            refreshAnnotations(); // show/hide the stroke time-range bars on the top timeline
            updateLeftChrome();
        }

        /// <summary>Switches the active Review tool (Select / Note / Line) and reflects it on the playfield + panel.</summary>
        private void applyReviewTool(ReviewTool tool)
        {
            reviewTool = tool;
            playfield.ReviewTool = tool;
            reviewToolPanel.SetActive(tool);
            // The object selection is kept across tool switches so a Select-then-Note flow can attach a timestamp.
        }

        /// <summary>Shows the playfield notes when reviewing (editable) or when the "show always" toggle is on (read-only).</summary>
        private void applyAnnotationVisibility()
        {
            bool show = reviewMode.Value || settings.ReviewShowAlways.Value;
            playfield.Annotations.Active = show;
            playfield.Annotations.Editable = reviewMode.Value;
            playfield.Annotations.FadeTo(show ? 1 : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        /// <summary>Creates a note at the clicked playfield position and opens the editor to type its text.</summary>
        private void createNoteAt(Vector2 osuPosition)
        {
            // Capture any objects the modder selected as the note's timestamp reference, then clear the selection.
            var refs = selection.Selected.Count > 0 ? new System.Collections.Generic.List<int>(selection.Selected) : null;

            var note = new Annotations.Annotation
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = Annotations.Annotation.KindNote,
                Time = Math.Round(CurrentTime),
                X = osuPosition.X,
                Y = osuPosition.Y,
                Author = reviewAuthorName(),
                Color = settings.ReviewAuthorColour.Value.ToHex(),
                Objects = refs,
            };
            // Held provisionally; only added to the document (as one undo step) once its text is committed.
            pendingNewNote = note;
            selection.Clear();
            noteEditPopover.OpenFor(note, isNew: true);
        }

        /// <summary>The logged-in osu! username, or null when offline (so the modder identity can be edited).</summary>
        private string? loggedInModderName()
            => auth?.IsLoggedIn == true && !string.IsNullOrWhiteSpace(auth.User.Value?.Username)
                ? auth!.User.Value!.Username
                : null;

        private string reviewAuthorName()
        {
            // Logged in: always the osu! account name (not editable). Offline: the typed Review name, else the
            // mapper name from settings, else a placeholder.
            string? locked = loggedInModderName();
            if (locked != null)
                return locked;

            string name = settings.ReviewAuthorName.Value.Trim();
            if (name.Length > 0)
                return name;
            name = settings.DefaultCreator.Value.Trim();
            return name.Length > 0 ? name : "Anon";
        }

        private void onNoteSaved(Annotations.Annotation note, string text, string type)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                onNoteDeleted(note); // an empty note is discarded (new) or removed (existing)
                return;
            }

            bool isNew = note == pendingNewNote;

            // Snapshot BEFORE mutating/adding so a single Ctrl+Z reverts the whole create/edit.
            pushUndo();
            note.Text = text;
            note.Type = type;
            note.Author = reviewAuthorName();
            note.Color = settings.ReviewAuthorColour.Value.ToHex();

            if (isNew)
            {
                reviewDoc.Annotations.Add(note);
                pendingNewNote = null;
            }

            refreshAnnotations();
            editable.IsDirty.Value = true;
        }

        private void onNoteDeleted(Annotations.Annotation note)
        {
            // A provisional (never-added) note just gets dropped - no undo step needed.
            if (note == pendingNewNote)
            {
                pendingNewNote = null;
                refreshAnnotations();
                return;
            }

            pushUndo();
            reviewDoc.Annotations.RemoveAll(a => a.Id == note.Id);
            refreshAnnotations();
            editable.IsDirty.Value = true;
        }

        /// <summary>Creates a static freehand stroke from a Draw-tool gesture, visible across a time range you can tune on the top timeline.</summary>
        private void createReviewStroke(System.Collections.Generic.IReadOnlyList<Vector2> points)
        {
            var pts = new System.Collections.Generic.List<float[]>(points.Count);
            foreach (var p in points)
                pts.Add(new[] { p.X, p.Y });

            double start = Math.Round(CurrentTime);
            var stroke = new Annotations.Annotation
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = Annotations.Annotation.KindShape,
                Time = start,
                EndTime = start + default_stroke_duration_ms,
                X = points[0].X,
                Y = points[0].Y,
                Author = reviewAuthorName(),
                Color = settings.ReviewAuthorColour.Value.ToHex(),
                Points = pts,
                Thickness = 3f,
            };
            pushUndo();
            reviewDoc.Annotations.Add(stroke);
            refreshAnnotations();
            editable.IsDirty.Value = true;
        }

        /// <summary>Clicking a stroke in Review mode removes it.</summary>
        private void onLineClicked(Annotations.Annotation stroke)
        {
            pushUndo();
            reviewDoc.Annotations.RemoveAll(a => a.Id == stroke.Id);
            refreshAnnotations();
            editable.IsDirty.Value = true;
            toasts?.Push("Stroke removed", EditorTheme.Colours.TextMuted);
        }

        // --- Timestamp navigation: clicking a note's timestamp seeks there and briefly selects its objects ---

        private long selectFlashToken;

        /// <summary>Selects the given objects, then clears the selection after a short beat (a visual "here it is" flash).</summary>
        private void flashSelectObjects(System.Collections.Generic.IReadOnlyCollection<int> ids)
        {
            if (ids.Count == 0)
                return;

            selection.SetRange(new System.Collections.Generic.HashSet<int>(ids));
            long token = ++selectFlashToken;
            Scheduler.AddDelayed(() =>
            {
                if (token == selectFlashToken)
                    selection.Clear();
            }, 1300);
        }

        /// <summary>A note's own timestamp chip: seek to it and flash-select the objects it references.</summary>
        private void onTimestampActivated(Annotations.Annotation a)
        {
            SeekTo(a.Time);
            if (a.Objects != null)
                flashSelectObjects(a.Objects);
        }

        /// <summary>An inline timestamp in note text: seek to its time and flash-select the objects with those combo numbers.</summary>
        private void onTextTimestampActivated(double time, System.Collections.Generic.List<int> combos)
        {
            SeekTo(time);
            flashSelectObjects(resolveComboObjects(time, combos));
        }

        /// <summary>Hovering an inline timestamp: pop a small black box previewing the objects it points at.</summary>
        private void showPatternPreview(double time, System.Collections.Generic.List<int> combos, Vector2 screenPos)
        {
            var objs = new System.Collections.Generic.List<HitObjectModel>();
            foreach (int id in resolveComboObjects(time, combos))
            {
                var o = parsed.HitObjects.Find(x => x.Id == id);
                if (o.Id >= 0)
                    objs.Add(o);
            }

            if (objs.Count == 0)
            {
                hidePatternPreview();
                return;
            }

            patternPreviewContent.Clear();
            patternPreviewContent.Add(new UI.PatternPreview(objs) { RelativeSizeAxes = Axes.Both });

            // Sit the box just above the hovered timestamp.
            Vector2 local = ToLocalSpace(screenPos);
            patternPreviewBox.Position = new Vector2(local.X, local.Y - 6);
            patternPreviewBox.Alpha = 1;
        }

        private void hidePatternPreview()
        {
            patternPreviewBox.Alpha = 0;
            patternPreviewContent.Clear();
        }

        /// <summary>Resolves combo numbers near a time to object ids (the first forward object per requested combo number).</summary>
        private System.Collections.Generic.List<int> resolveComboObjects(double time, System.Collections.Generic.List<int> combos)
        {
            var result = new System.Collections.Generic.List<int>();
            if (combos.Count == 0 || parsed.HitObjects.Count == 0)
                return result;

            // parsed.HitObjects is kept time-sorted; start from the object nearest the timestamp time.
            int start = 0;
            double best = double.MaxValue;
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                double d = Math.Abs(parsed.HitObjects[i].StartTime - time);
                if (d < best) { best = d; start = i; }
            }

            var need = new System.Collections.Generic.HashSet<int>(combos);
            for (int i = start; i < parsed.HitObjects.Count && need.Count > 0; i++)
            {
                if (need.Remove(parsed.HitObjects[i].ComboNumber))
                    result.Add(parsed.HitObjects[i].Id);
            }
            return result;
        }

        /// <summary>Saves the Review layer locally (called from the map save). No-op when there's nothing to keep.</summary>
        private void saveReviewLayer()
        {
            reviewDoc.OsuFileHash = difficulty.OsuFileHash;
            reviewDoc.SetOnlineId = set.OnlineID;
            reviewDoc.Difficulty = difficulty.DifficultyName;
            reviewDoc.Title = set.Title;
            reviewDoc.Artist = set.Artist;
            reviewDoc.Author = reviewAuthorName();
            reviewDoc.AuthorColor = settings.ReviewAuthorColour.Value.ToHex();
            reviewStore.SaveLocal(reviewKey, reviewDoc);
        }

        /// <summary>Exports the Review layer to a shareable .sobemod file and reveals it.</summary>
        private void exportReviewLayer()
        {
            if (reviewDoc.Annotations.Count == 0)
            {
                toasts?.Push("Nothing to export - add some notes first", EditorTheme.Colours.Warning);
                return;
            }

            saveReviewLayer();
            string exportsDir = host.Storage.GetFullPath("exports");
            string name = $"{set.Artist} - {set.Title} [{difficulty.DifficultyName}] {reviewAuthorName()}";
            string? path = Annotations.AnnotationStore.ExportToFile(reviewDoc, exportsDir, name);
            if (path != null)
            {
                toasts?.Push($"Exported {System.IO.Path.GetFileName(path)}", EditorTheme.Colours.Success);
                host.PresentFileExternally(path);
            }
            else
            {
                toasts?.Push("Export failed", EditorTheme.Colours.Error);
            }
        }

        private void importReviewLayerPrompt()
            => toasts?.Push("Drag a .sobemod file onto the window to import it", EditorTheme.Colours.Info);

        private void onReviewFileDropped(string path)
        {
            if (!this.IsCurrentScreen() || System.IO.Path.GetExtension(path).ToLowerInvariant() != Annotations.AnnotationStore.FileExtension)
                return;

            var imported = Annotations.AnnotationStore.ImportFromFile(path);
            if (imported == null)
            {
                toasts?.Push("Couldn't read that .sobemod file", EditorTheme.Colours.Error);
                return;
            }

            pushUndo();
            int added = reviewDoc.Merge(imported);
            refreshAnnotations();
            editable.IsDirty.Value = true;

            if (!reviewMode.Value)
                reviewMode.Value = true;

            string warn = !string.IsNullOrEmpty(imported.OsuFileHash) && imported.OsuFileHash != difficulty.OsuFileHash
                ? " (note: it was authored on a different version of this map)" : string.Empty;
            toasts?.Push($"Imported {added} annotation{(added == 1 ? "" : "s")} from {imported.Author}{warn}",
                added > 0 ? EditorTheme.Colours.Success : EditorTheme.Colours.Info);
        }

        private static Color4 toColor4(Colour4 c) => new Color4(c.R, c.G, c.B, c.A);
        private static Colour4 toColour4(Color4 c) => new Colour4(c.R, c.G, c.B, c.A);

        /// <summary>Shows/hides the Auto-cursor popover and keeps it positioned under the mod chips.</summary>
        private void toggleHumanizeTuning()
        {
            if (humanizeTuningPanel.State.Value == Visibility.Visible)
                humanizeTuningPanel.Hide();
            else
                humanizeTuningPanel.Show();
        }

        private void toggleAutoMenu() => setAutoMenuOpen(!autoMenuOpen);

        private void setAutoMenuOpen(bool open)
        {
            autoMenuOpen = open;
            autoMenu.FadeTo(open ? 1 : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        /// <summary>
        /// Updates the left chrome's visibility from both flags: the tool column shows only outside Modding Mode and
        /// when the hitsound-lanes editor is closed; the left-side hitsound controls block shows when the lanes are
        /// open (but never while modding). Centralised so the two toggles don't fight over the left column.
        /// </summary>
        private void updateLeftChrome()
        {
            bool lanes = hitsoundMode.Value;
            bool modding = moddingMode.Value;
            bool review = reviewMode.Value;
            leftPanels.FadeTo(modding || lanes || review ? 0 : 1, 150, Easing.OutQuad);
            hitsoundControlsBlock.FadeTo(!modding && !review && lanes ? 1 : 0, 150, Easing.OutQuad);
        }

        /// <summary>Entering/leaving Modding Mode: force Auto-only, reserve the right panel, fetch discussions once.</summary>
        private void onModdingModeChanged(bool on)
        {
            // Auto-only: the mod-preview toggles are turned off and their chips disabled while modding.
            if (on)
            {
                hardRockMod.Value = false;
                hiddenMod.Value = false;
                easyMod.Value = false;
                rateMod.Value = RateMod.Off;
            }
            hardRockButton.SetEnabled(!on);
            hiddenButton.SetEnabled(!on);
            easyButton.SetEnabled(!on);
            rateModButton.SetEnabled(!on);

            // The left composing tool/hitsound column + bank bar are hidden while reviewing mods, but the
            // top-left Settings / Export / Song Setup buttons stay available (they're useful while modding too).
            updateLeftChrome();

            // The panel slide + playfield inset are eased per-frame in updateHitsoundLayout; nothing to fade here.
            if (on)
            {
                if (!discussionsRequested)
                    loadDiscussions();
                else
                    refreshModdingViews();
            }
            else
            {
                topTimeline.SetDiscussions(System.Array.Empty<Online.ModdingDiscussion>());
            }
        }

        // Short-lived cache of fetched discussions, keyed by beatmapset id, shared across editor sessions so
        // reopening the same map (or toggling Modding Mode) doesn't re-hit the backend every time. Discussions
        // change slowly, so a couple of minutes of staleness is fine.
        private static readonly TimeSpan discussions_ttl = TimeSpan.FromMinutes(2);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (System.DateTime Fetched, System.Collections.Generic.List<Online.ModdingDiscussion> Data)> discussionsCache = new();

        /// <summary>Fetches the beatmapset's discussions (once) from the backend and populates the modding views.</summary>
        private void loadDiscussions()
        {
            discussionsRequested = true;
            int setId = set.OnlineID;

            // Serve a fresh cached copy immediately, skipping the network round-trip entirely.
            if (setId > 0 && discussionsCache.TryGetValue(setId, out var cached)
                && System.DateTime.UtcNow - cached.Fetched < discussions_ttl)
            {
                loadedDiscussions = cached.Data;
                moddingPanel.SetDiscussions(cached.Data);
                refreshModdingViews();
                return;
            }

            Task.Run(async () =>
            {
                var discussions = await Online.SobeApi.GetDiscussionsAsync(setId).ConfigureAwait(false);
                if (setId > 0)
                    discussionsCache[setId] = (System.DateTime.UtcNow, discussions);
                Schedule(() =>
                {
                    loadedDiscussions = discussions;
                    moddingPanel.SetDiscussions(discussions);
                    refreshModdingViews();
                });
            });
        }

        /// <summary>Re-applies the left-panel filters to the messages list and the timeline bubbles.</summary>
        private void refreshModdingViews()
        {
            if (loadedDiscussions == null)
                return;

            var visible = loadedDiscussions.Where(moddingPanel.IsVisible).ToList();
            moddingPanel.SetMessages(visible);
            topTimeline.SetDiscussions(visible);
        }

        /// <summary>Re-applies the active mod-preview state to the playfield (diameter/preempt + flip + fade).</summary>
        private void onModsChanged()
        {
            playfield.SetMods(hardRockMod.Value, hiddenMod.Value);
            rebuildHitObjects();
        }

        /// <summary>Circle size after the active mods: HardRock ×1.3 (capped at 10), Easy ×0.5 (mutually exclusive).</summary>
        private float effectiveCs()
        {
            if (hardRockMod.Value) return Math.Min(10f, editable.Cs.Value * 1.3f);
            if (easyMod.Value) return editable.Cs.Value * 0.5f;
            return editable.Cs.Value;
        }

        /// <summary>Approach rate after the active mods: HardRock ×1.4 (capped at 10), Easy ×0.5 (mutually exclusive).</summary>
        private float effectiveAr()
        {
            if (hardRockMod.Value) return Math.Min(10f, editable.Ar.Value * 1.4f);
            if (easyMod.Value) return editable.Ar.Value * 0.5f;
            return editable.Ar.Value;
        }

        /// <summary>
        /// Approach-circle preempt window (ms) for the mod-adjusted AR. DoubleTime is NOT applied here - it speeds
        /// up the playback clock instead, which makes the (unchanged) preempt window elapse 1.5x faster on its own.
        /// </summary>
        private double effectivePreempt() => ParsedBeatmap.PreemptFor(effectiveAr());

        /// <summary>
        /// Spacing (osu!pixels) between a slider's ticks: <c>100 · SliderMultiplier · SV / SliderTickRate</c>
        /// (osu!'s scoring distance per tick), independent of BPM. 0 when the slider has no ticks.
        /// </summary>
        private double sliderTickDistance(HitObjectModel o)
        {
            if (o.Kind != HitObjectKind.Slider)
                return 0;

            double tickRate = editable.SliderTickRate.Value;
            double mult = editable.SliderMultiplier.Value;
            if (tickRate <= 0 || mult <= 0)
                return 0;

            return 100.0 * mult * velocityAt(o.StartTime) / tickRate;
        }

        /// <summary>The smoothed playback time (ms), interpolated between coarse audio-clock updates.</summary>
        private double CurrentTime => audioClock?.CurrentTime ?? track?.CurrentTime ?? 0;

        /// <summary>
        /// Playback time fed to the visual playhead, objects and timelines: <see cref="CurrentTime"/> shifted by the
        /// user's audio offset while playing, so what's on screen matches what's heard through a laggy output. Equals
        /// <see cref="CurrentTime"/> while paused (no offset), so it never moves where placed objects land.
        /// </summary>
        private double VisualTime => visualClock?.CurrentTime ?? CurrentTime;

        /// <summary>
        /// Advances the raw audio clock and the offset visual clock (one per frame, and after a seek). The offset
        /// is only applied while the track is running - paused, the playhead must sit exactly where edits land.
        /// </summary>
        private void advanceClocks()
        {
            audioClock?.ProcessFrame();

            if (visualClock != null)
            {
                visualClock.Offset = track?.IsRunning == true ? settings.AudioOffset.Value : 0;
                visualClock.ProcessFrame();
            }
        }

        protected override void Update()
        {
            base.Update();
            updateHitsoundLayout();
            updatePlaybackThrottle();
            // Advance the clocks once per frame, before children read CurrentTime / VisualTime.
            advanceClocks();
            updateHitsounds();
            updateLoopingSamples();
            updateBpm();
            toolPanel.SetActive(currentTool());

            distanceSnapText.Alpha = distanceSnapEnabled ? 1 : 0;
            if (distanceSnapEnabled && distanceSpacing != lastDistanceSpacing)
            {
                lastDistanceSpacing = distanceSpacing;
                distanceSnapText.Text = $"Distance snap: {distanceSpacing:0.0}x";
            }

            updatePlacementComboPreview();
            tickRotationAnimation();

            if (spinnerBuilding)
                updateSpinnerPreview();
        }

        // Inactive-window frame caps. Hitsounds are played per-frame, so at the very low idle cap they bunch up
        // to the frame boundary and sound off the beat. We therefore only keep that aggressive cap while paused
        // (no hitsounds play then anyway); during playback we raise it so tabbed-away hitsound timing stays tight.
        private const int inactive_hz_idle = 20;
        private const int inactive_hz_playing = 240;
        private bool? lastPlaybackThrottleRunning;

        /// <summary>
        /// Keeps the window's inactive frame cap in step with playback: a hard cap when paused (max power saving,
        /// and no hitsounds are firing) and a high cap while playing so hitsound timing stays accurate even when
        /// the editor is tabbed away. Driven off the actual track state so it covers every start/stop, including
        /// the track finishing on its own.
        /// </summary>
        private void updatePlaybackThrottle()
        {
            bool running = track?.IsRunning == true;
            if (running == lastPlaybackThrottleRunning)
                return;

            lastPlaybackThrottleRunning = running;
            host.MaximumInactiveHz = running ? inactive_hz_playing : inactive_hz_idle;
        }

        /// <summary>
        /// Drives the expand/collapse of the hitsound-lanes editor: when active the top timeline grows by three
        /// compact <see cref="hitsound_lane_height"/> lane rows (the playfield shrinks to suit), and the compose
        /// area's top padding follows. Eased per-frame (no transform on padding) so the move reads as a smooth slide.
        /// </summary>
        private void updateHitsoundLayout()
        {
            float target = top_bar_height;
            if (hitsoundMode.Value)
                // The object band (top_bar_height) plus three compact lane rows.
                target = top_bar_height + 3 * hitsound_lane_height;

            // Hidden HUD: collapse the reserved bars to a small inset so the playfield recentres and grows slightly.
            if (hudHidden)
                target = hidden_bar_inset;

            if (Math.Abs(topHeightCurrent - target) < 0.5f)
                topHeightCurrent = target;
            else
                topHeightCurrent = (float)Interpolation.Lerp(target, topHeightCurrent, Math.Exp(-0.018 * Time.Elapsed));

            float bottomTarget = hudHidden ? hidden_bar_inset : bottom_bar_height;
            if (Math.Abs(bottomHeightCurrent - bottomTarget) < 0.5f)
                bottomHeightCurrent = bottomTarget;
            else
                bottomHeightCurrent = (float)Interpolation.Lerp(bottomTarget, bottomHeightCurrent, Math.Exp(-0.018 * Time.Elapsed));

            // Modding Mode reserves room on the right; ease the playfield (and the right-anchored HUD) inward.
            float moddingTarget = moddingMode.Value ? modding_panel_width + modding_panel_gap : 0f;
            if (Math.Abs(moddingInsetCurrent - moddingTarget) < 0.5f)
                moddingInsetCurrent = moddingTarget;
            else
                moddingInsetCurrent = (float)Interpolation.Lerp(moddingTarget, moddingInsetCurrent, Math.Exp(-0.018 * Time.Elapsed));

            // The panel docks to the right edge (inset by the gap) once fully in; off-screen right when fully out.
            moddingPanel.X = modding_panel_width - moddingInsetCurrent;
            float hudRight = 16 + moddingInsetCurrent;

            topTimeline.Height = topHeightCurrent;
            composeContainer.Padding = new MarginPadding { Top = topHeightCurrent, Bottom = bottomHeightCurrent, Right = moddingInsetCurrent };

            // The top bar (left panel + timeline + settings panel) is a fixed block - the modding panel never
            // moves it (it slides in over the compose area below). So nothing up here tracks moddingInset.

            // The mod chips sit just below the timeline, aligned under the settings panel's column (flush right);
            // the Auto popover / distance-snap readout sit below the (wrapping) chip grid. These DO slide left of
            // the modding panel since they live in the compose area.
            float chipsTop = topHeightCurrent + 8;
            modButtons.Margin = new MarginPadding { Right = moddingInsetCurrent, Top = chipsTop };
            float belowChips = chipsTop + modButtons.DrawHeight + 6;
            autoMenu.Margin = new MarginPadding { Right = hudRight, Top = belowChips };
            distanceSnapText.Margin = new MarginPadding { Right = hudRight, Top = belowChips };
            // Live tuning panel sits just left of the AU mini-menu so both stay visible while dragging sliders.
            humanizeTuningPanel.Margin = new MarginPadding { Right = hudRight + autoMenu.DrawWidth + 12, Top = belowChips };

            // The left-side hitsound controls block spans the lanes band (below the toolbar) while the lanes are open.
            hitsoundControlsBlock.Height = Math.Max(0, topHeightCurrent - top_bar_height);
        }

        // Cache key for the live combo preview shown while a placement tool is armed (snapped time + state).
        private (int time, bool newCombo, int count)? lastPreviewKey;

        /// <summary>
        /// While a placement tool is armed, renumbers the existing objects live as if the pending object were
        /// already inserted at the current snapped time - mirroring osu!lazer, where the placement object is a
        /// real (pending) member of the beatmap. Reverts to the committed numbering when the tool is disarmed.
        /// </summary>
        private void updatePlacementComboPreview()
        {
            bool armed = playfield.PlacementActive || playfield.SliderPlacementActive;

            if (!armed)
            {
                if (lastPreviewKey != null)
                {
                    lastPreviewKey = null;
                    rebuildHitObjects(); // restore committed combo numbers
                }
                return;
            }

            int t = (int)Math.Round(snapTime(CurrentTime));
            var key = (t, playfield.NewComboArmed, parsed.HitObjects.Count);
            if (lastPreviewKey == key)
                return;

            lastPreviewKey = key;

            // Insert a phantom object at the pending time, recompute combos, then drop it - the remaining
            // (real) objects carry the previewed numbering. The ghost circle shows its own number separately.
            var temp = new List<HitObjectModel>(parsed.HitObjects);
            int phantomType = playfield.NewComboArmed ? 0b101 : 0b001;
            temp.Add(new HitObjectModel(0, 0, t, HitObjectKind.Circle, null,
                RawLine: $"0,0,{t},{phantomType},0,0:0:0:0:", Id: int.MinValue));
            temp.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            applyCombosTo(temp);
            temp.RemoveAll(o => o.Id == int.MinValue);

            playfield.SetHitObjects(temp, circleDiameter(), effectivePreempt());
        }

        /// <summary>The composing tool the playfield currently has armed.</summary>
        private EditorTool currentTool() =>
            playfield.PlacementActive ? EditorTool.Circle
            : playfield.SliderPlacementActive ? EditorTool.Slider
            : playfield.SpinnerPlacementActive ? EditorTool.Spinner
            : EditorTool.Selection;

        /// <summary>Arms the chosen tool from the toolbox.</summary>
        private void applyTool(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Selection:
                    playfield.SetPlacementActive(false);
                    playfield.SetSliderPlacementActive(false);
                    playfield.SetSpinnerPlacementActive(false);
                    break;

                case EditorTool.Circle:
                    playfield.SetPlacementActive(true);
                    break;

                case EditorTool.Slider:
                    playfield.SetSliderPlacementActive(true);
                    break;

                case EditorTool.Spinner:
                    playfield.SetSpinnerPlacementActive(true);
                    break;
            }
        }

        // Last BPM/SV rendered, so the readout strings are only rebuilt when the value actually changes
        // (this runs every frame during playback; re-formatting identical text allocated garbage each frame).
        private double lastBpmBeatLength = double.NaN;
        private double lastBpmRate = double.NaN;
        private double lastSv = double.NaN;

        /// <summary>Shows the BPM of the timing section under the current playback position.</summary>
        private void updateBpm()
        {
            double now = CurrentTime;

            // Binary search the time-sorted timing points for the active BPM (last one at/before `now`),
            // falling back to the first point when the playhead sits before the map's first timing point.
            // Runs every frame, so O(log points) instead of scanning the whole list.
            var bps = parsed.BeatPoints;
            int lo = 0, hi = bps.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (bps[mid].Time <= now)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            double beatLength = bps.Count == 0 ? 0 : bps[Math.Max(0, lo - 1)].BeatLength;

            // DT/NC speed the playback up by 1.5x, which also raises the effective BPM - show the sped-up value.
            double rate = rateMod.Value == RateMod.Off ? 1.0 : rate_mod_speed;

            if (beatLength != lastBpmBeatLength || rate != lastBpmRate)
            {
                lastBpmBeatLength = beatLength;
                lastBpmRate = rate;
                bpmText.Text = beatLength > 0
                    ? (rate > 1.0
                        ? $"{60000.0 / beatLength * rate:0.##} BPM ({rate:0.##}x)"
                        : $"{60000.0 / beatLength:0.##} BPM")
                    : string.Empty;
            }

            double sv = velocityAt(now);
            if (sv != lastSv)
            {
                lastSv = sv;
                svText.Text = $"{sv:0.##}x SV";
            }
        }

        /// <summary>Plays each hitsound event as playback passes its time (only while playing).</summary>
        private void updateHitsounds()
        {
            if (hitsounds == null || track == null)
                return;

            double now = CurrentTime;

            // Paused: don't play, and resync the cursor on the next resume.
            if (!track.IsRunning)
            {
                needHitsoundResync = true;
                lastHitsoundTime = now;
                return;
            }

            // A seek (time jumped backward or forward by a lot) skips ahead without machine-gunning.
            if (needHitsoundResync || now < lastHitsoundTime || now - lastHitsoundTime > 300)
            {
                hitsoundIndex = 0;
                while (hitsoundIndex < sampleEvents.Count && sampleEvents[hitsoundIndex].Time <= now)
                    hitsoundIndex++;
                needHitsoundResync = false;
            }
            else
            {
                while (hitsoundIndex < sampleEvents.Count && sampleEvents[hitsoundIndex].Time <= now)
                {
                    var e = sampleEvents[hitsoundIndex];
                    if (e.Kind == SampleEventKind.SliderTick)
                        hitsounds.PlaySliderTick(e.Normal, e.Volume, e.Index);
                    else
                        hitsounds.Play(e.HitSound, e.Normal, e.Addition, e.Volume, e.Index, e.Filename);
                    hitsoundIndex++;
                }
            }

            lastHitsoundTime = now;
        }

        /// <summary>
        /// Starts/stops the continuous body loops (<c>sliderslide</c>/<c>sliderwhistle</c>/<c>spinnerspin</c>) so exactly
        /// the objects the playhead is currently inside are sounding. Recomputed every frame from <see cref="CurrentTime"/>,
        /// so it is seek/scrub-safe: seeking into a slider starts its loop, seeking out (or pausing) stops it, with no
        /// stateful integration. Mirrors osu!lazer, where each DrawableSlider/Spinner owns a looping skinnable sample.
        /// </summary>
        private void updateLoopingSamples()
        {
            desiredLoops.Clear();

            if (hitsounds != null && track != null && track.IsRunning)
            {
                double now = CurrentTime;
                foreach (var o in parsed.HitObjects)
                {
                    if (o.Kind != HitObjectKind.Slider && o.Kind != HitObjectKind.Spinner)
                        continue;

                    double end = o.StartTime + o.Duration;
                    if (now < o.StartTime || now >= end)
                        continue;

                    SampleBank bank = resolveBanksAt(o.StartTime, o.NormalBank, o.AdditionBank).Normal;
                    int index = effectiveIndex(o.SampleIndex, o.StartTime);
                    float volume = eventVolume(o, now);

                    if (o.Kind == HitObjectKind.Slider)
                    {
                        ensureLoop($"{o.Id}:slide", bank, "sliderslide", index, volume);
                        // The slider body also whistles for the whole slide when its hitsound carries the whistle bit.
                        if ((o.HitSound & 0b0010) != 0)
                            ensureLoop($"{o.Id}:whistle", bank, "sliderwhistle", index, volume);
                    }
                    else
                    {
                        // spinnerspin is a bank-less skin sample (no normal-/soft-/drum- prefix).
                        ensureLoopRaw($"{o.Id}:spin", "spinnerspin", volume);
                    }
                }
            }

            // Stop any loop that's playing but no longer wanted (object passed, paused, seeked away, deleted, edited).
            if (activeLoops.Count > 0)
            {
                loopsToStop.Clear();
                foreach (var key in activeLoops.Keys)
                    if (!desiredLoops.Contains(key))
                        loopsToStop.Add(key);

                foreach (var key in loopsToStop)
                {
                    stopLoop(activeLoops[key]);
                    activeLoops.Remove(key);
                }
            }
        }

        /// <summary>Ensures a bank loop (sliderslide/sliderwhistle) is playing, keeping its volume fresh for live green-point changes.</summary>
        private void ensureLoop(string key, SampleBank bank, string name, int index, float volume)
        {
            desiredLoops.Add(key);
            if (activeLoops.TryGetValue(key, out var existing))
            {
                existing.Volume.Value = volume;
                return;
            }
            // Resolve the channel only on the first frame the loop is wanted (not every frame it stays active).
            startLoop(key, hitsounds!.GetLoopChannel(bank, name, index), volume);
        }

        /// <summary>Ensures a bank-less loop (spinnerspin) is playing.</summary>
        private void ensureLoopRaw(string key, string name, float volume)
        {
            desiredLoops.Add(key);
            if (activeLoops.TryGetValue(key, out var existing))
            {
                existing.Volume.Value = volume;
                return;
            }
            startLoop(key, hitsounds!.GetLoopChannel(name), volume);
        }

        private void startLoop(string key, SampleChannel? channel, float volume)
        {
            if (channel == null)
                return;

            channel.Looping = true;
            channel.Volume.Value = volume;
            // Follow the DT/NC rate mods so the loop speeds up with the track, like the one-shot hitsounds.
            channel.AddAdjustment(AdjustableProperty.Tempo, dtTempoRate);
            channel.AddAdjustment(AdjustableProperty.Frequency, ncFrequencyRate);
            channel.Play();
            activeLoops[key] = channel;
        }

        private void stopLoop(SampleChannel channel)
        {
            channel.RemoveAdjustment(AdjustableProperty.Tempo, dtTempoRate);
            channel.RemoveAdjustment(AdjustableProperty.Frequency, ncFrequencyRate);
            channel.Stop();
        }

        /// <summary>Stops every active body loop (on dispose, or when audio is torn down).</summary>
        private void stopAllLoops()
        {
            foreach (var channel in activeLoops.Values)
                stopLoop(channel);
            activeLoops.Clear();
            desiredLoops.Clear();
        }

        /// <summary>
        /// Rebuilds the time-sorted hitsound event list: a circle plays at its start, a spinner at its end,
        /// and a slider once per node (head, each repeat, tail) using its per-node sample banks.
        /// </summary>
        private void rebuildSampleEvents()
        {
            sampleEvents.Clear();

            foreach (var o in parsed.HitObjects)
            {
                switch (o.Kind)
                {
                    case HitObjectKind.Spinner:
                    {
                        double t = o.StartTime + o.Duration;
                        var (n, a) = resolveBanksAt(t, o.NormalBank, o.AdditionBank);
                        sampleEvents.Add(new SampleEvent(t, SampleEventKind.Hit, o.HitSound, n, a, eventVolume(o, t), effectiveIndex(o.SampleIndex, t), o.SampleFilename));
                        break;
                    }

                    case HitObjectKind.Slider:
                        int nodes = Math.Max(1, o.Slides) + 1;
                        double perNode = o.Slides > 0 ? o.Duration / o.Slides : 0;
                        for (int i = 0; i < nodes; i++)
                        {
                            var ns = o.NodeSamples != null && i < o.NodeSamples.Count
                                ? o.NodeSamples[i]
                                : new NodeSample(o.HitSound, o.NormalBank, o.AdditionBank);
                            double t = o.StartTime + perNode * i;
                            var (n, a) = resolveBanksAt(t, ns.NormalBank, ns.AdditionBank);
                            // The custom file sample (if any) applies to the slider's head node only; index applies to every node.
                            sampleEvents.Add(new SampleEvent(t, SampleEventKind.Hit, ns.HitSound, n, a, eventVolume(o, t), effectiveIndex(o.SampleIndex, t), i == 0 ? o.SampleFilename : ""));
                        }
                        addSliderTicks(o);
                        break;

                    default:
                    {
                        var (n, a) = resolveBanksAt(o.StartTime, o.NormalBank, o.AdditionBank);
                        sampleEvents.Add(new SampleEvent(o.StartTime, SampleEventKind.Hit, o.HitSound, n, a, eventVolume(o, o.StartTime), effectiveIndex(o.SampleIndex, o.StartTime), o.SampleFilename));
                        break;
                    }
                }
            }

            sampleEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            needHitsoundResync = true;
        }

        /// <summary>
        /// The playback volume for a sample at <paramref name="time"/>: the object's explicit hitSample volume
        /// override when it set one (<see cref="HitObjectModel.SampleVolume"/> &gt; 0), otherwise the timing
        /// model's volume active at that time (<see cref="volumeAt"/>). This makes a 5%-volume green point placed
        /// at a slider's end silence only that tail/tick, while the next object follows its own active point.
        /// </summary>
        private float eventVolume(HitObjectModel o, double time) => o.SampleVolume > 0 ? o.SampleVolume : volumeAt(time);

        /// <summary>
        /// Schedules a slider's tick sounds (osu!'s <c>slidertick</c>): ticks fall every <see cref="sliderTickDistance"/>
        /// osu!pixels along each span, mapped to time by the fraction of the span length, mirrored on reverse spans.
        /// Span endpoints (the nodes) are excluded - those play their own node hitsound.
        /// </summary>
        private void addSliderTicks(HitObjectModel o)
        {
            double tickDist = sliderTickDistance(o);
            if (tickDist <= 0 || o.Path is not { Count: > 1 } path)
                return;

            double spanLength = SliderGeometry.PathLength(path);
            if (spanLength <= tickDist)
                return;

            int slides = Math.Max(1, o.Slides);
            double spanDuration = o.Duration / slides;

            for (int span = 0; span < slides; span++)
            {
                bool reverse = (span & 1) == 1;
                double spanStart = o.StartTime + span * spanDuration;

                // ticks at tickDist, 2·tickDist, ... up to (but not touching) the span end node.
                for (double d = tickDist; d < spanLength - 1e-3; d += tickDist)
                {
                    double frac = d / spanLength;
                    double t = reverse ? spanStart + (1 - frac) * spanDuration : spanStart + frac * spanDuration;
                    // The slider tick plays in the slider's resolved normal bank and sample index.
                    SampleBank tickBank = resolveBanksAt(t, o.NormalBank, o.AdditionBank).Normal;
                    sampleEvents.Add(new SampleEvent(t, SampleEventKind.SliderTick, 0, tickBank, SampleBank.Normal, eventVolume(o, t), effectiveIndex(o.SampleIndex, t)));
                }
            }
        }

        private void rebuildHitObjects()
        {
            playfield.SetHitObjects(parsed.HitObjects, circleDiameter(), effectivePreempt());
        }

        /// <summary>
        /// Hit-circle diameter in osu!pixels for the current CS (standard formula <c>(54.4 - 4.48·CS)·2</c>),
        /// minus a manual visual override so our circles match osu!lazer's editor, which renders them a few
        /// pixels smaller than the raw formula gives.
        /// </summary>
        private float circleDiameter() => (54.4f - 4.48f * effectiveCs()) * 2 - circle_diameter_override;

        /// <summary>osu!pixels shaved off the hit-circle diameter to match lazer's editor (see <see cref="circleDiameter"/>).</summary>
        private const float circle_diameter_override = 5f;

        // --- IEditorActions: editing operations invoked by the timeline/playfield ---

        /// <summary>Inserts a new hit circle at the given osu!pixel position on the current snapped time.</summary>
        public void PlaceCircle(Vector2 osuPosition)
        {
            int time = (int)Math.Round(snapTime(CurrentTime));
            osuPosition = SnapPlacement(osuPosition);

            int x = Math.Clamp((int)Math.Round(osuPosition.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH);
            int y = Math.Clamp((int)Math.Round(osuPosition.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT);

            int id = nextId();

            // Type bit 0 = circle; bit 2 = new combo (set while Q is armed).
            int type = playfield.NewComboArmed ? 0b101 : 0b001;
            string raw = $"{x},{y},{time},{type},{pendingHitSound},{pendingSampleField()}";

            pushUndo();
            removeObjectsAt(time);
            parsed.HitObjects.Add(new HitObjectModel(x, y, time, HitObjectKind.Circle, null, RawLine: raw, Id: id,
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank,
                SampleVolume: pendingSampleVolume, SampleIndex: pendingSampleIndex));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            // Leave the new object unselected: keeping it selected made Q (new combo) act on the freshly
            // placed circle instead of arming the next placement, which surprised the mapper.
            selection.Clear();
            // "New combo" is a one-shot for the object just placed: once it carries the new combo, disarm so the
            // following circles continue the combo (matching the mapper's expectation and the phantom preview).
            playfield.NewComboArmed = false;
        }

        /// <summary>Inserts a new slider through the given control points (head first) on the current snapped time.</summary>
        // The slider's start time is locked in when the head is placed (BeginSliderPlacement), so scrubbing the
        // timeline while still adding anchors doesn't re-snap the length or move the slider.
        private double? sliderPlaceTime;

        public void BeginSliderPlacement() => sliderPlaceTime = Math.Round(snapTime(CurrentTime));

        public void EndSliderPlacement() => sliderPlaceTime = null;

        /// <summary>The locked-in slider start time during a build, or the live snapped time if none is set.</summary>
        private int placementTime() => (int)(sliderPlaceTime ?? Math.Round(snapTime(CurrentTime)));

        /// <summary>
        /// The minimum drawn length (osu!px) for a slider to be placeable: one beat-snap division of travel
        /// (= lazer's beat-snap distance). lazer rounds the placed length DOWN to a multiple of this, so a slider
        /// shorter than the first tick snaps to zero length and renders nothing - we mirror that by refusing to
        /// build/commit until the drawn curve reaches the first tick.
        /// </summary>
        private double placementMinLength(double time) => 100.0 * parsed.SliderMultiplier * velocityAt(time) / beatDivisor.Value.Value;

        public void PlaceSlider(IReadOnlyList<SliderControlPoint> points)
        {
            var cps = SliderGeometry.InferSegmentTypes(clampControlPoints(points));
            if (cps.Count < 2)
                return;

            int time = placementTime();

            // The freshly-traced slider spans its full control polygon; snap that length so the tail lands on a tick.
            double drawn = SliderGeometry.PathLength(SliderGeometry.ComputePath(cps));
            if (drawn < placementMinLength(time))
                return; // shorter than the first tick - lazer treats this as invalid for placement

            double pixelLength = snapSliderLength(time, drawn);
            var fullPath = SliderGeometry.ComputePath(cps, pixelLength);
            if (pixelLength < 1 || fullPath.Count < 2)
                return;

            int hx = (int)Math.Round(cps[0].X);
            int hy = (int)Math.Round(cps[0].Y);
            int id = nextId();

            // Type bit 1 = slider; bit 2 = new combo (set while Q is armed).
            int type = playfield.NewComboArmed ? 0b110 : 0b010;
            string length = pixelLength.ToString("0.###", CultureInfo.InvariantCulture);
            string curve = SliderGeometry.CurveField(cps);
            // A freshly-placed slider has one span = two nodes (head, tail), both using the pending hitsounds.
            string edgeSounds = $"{pendingHitSound}|{pendingHitSound}";
            string edgeSets = $"{pendingSet()}|{pendingSet()}";
            var nodeSamples = new[]
            {
                new NodeSample(pendingHitSound, pendingNormalBank, pendingAdditionBank),
                new NodeSample(pendingHitSound, pendingNormalBank, pendingAdditionBank),
            };
            // x,y,time,type,hitSound,sliderType|points...,slides,length,edgeSounds,edgeSets,hitSample
            string raw = $"{hx},{hy},{time},{type},{pendingHitSound},{curve},1,{length},{edgeSounds},{edgeSets},{pendingSampleField()}";

            double duration = sliderDuration(time, pixelLength, 1);

            pushUndo();
            removeObjectsAt(time);
            parsed.HitObjects.Add(new HitObjectModel(hx, hy, time, HitObjectKind.Slider, fullPath, duration, 1,
                RawLine: raw, Id: id, ControlPoints: cps, NodeSamples: nodeSamples,
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank,
                SampleVolume: pendingSampleVolume, SampleIndex: pendingSampleIndex));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            // Leave the new slider unselected (see PlaceCircle) so Q keeps arming the next placement.
            selection.Clear();
            // New combo is a one-shot: the slider just placed carries it, so disarm for the following objects.
            playfield.NewComboArmed = false;
            sliderPlaceTime = null;
        }

        // --- Spinner placement (osu!lazer model): click sets the start, the end follows the playhead. ---

        private bool spinnerBuilding;
        private int spinnerStartTime;

        public void BeginSpinnerPlacement()
        {
            spinnerStartTime = (int)Math.Round(snapTime(CurrentTime));
            spinnerBuilding = true;
            updateSpinnerPreview();
        }

        public void FinishSpinnerPlacement()
        {
            if (!spinnerBuilding)
                return;

            spinnerBuilding = false;
            topTimeline.ClearSliderPreview();

            int end = spinnerEndTime();
            createSpinner(spinnerStartTime, end);
        }

        public void CancelSpinnerPlacement()
        {
            spinnerBuilding = false;
            topTimeline.ClearSliderPreview();
        }

        /// <summary>The spinner's end time as it's being placed: the snapped playhead, but at least one beat long.</summary>
        private int spinnerEndTime()
        {
            double minEnd = spinnerStartTime + beatLengthAt(spinnerStartTime);
            return (int)Math.Round(Math.Max(minEnd, snapTime(CurrentTime)));
        }

        /// <summary>While placing a spinner, shows its growing time extent on the top timeline (a beats readout).</summary>
        private void updateSpinnerPreview()
        {
            int end = spinnerEndTime();
            topTimeline.ShowSliderPreview(spinnerStartTime, end - spinnerStartTime, beatLengthAt(spinnerStartTime));
        }

        /// <summary>Inserts a centred spinner spanning the given start/end times (osu! stable format).</summary>
        private void createSpinner(int start, int end)
        {
            int cx = (int)(ParsedBeatmap.PLAYFIELD_WIDTH / 2f);  // 256
            int cy = (int)(ParsedBeatmap.PLAYFIELD_HEIGHT / 2f); // 192
            int id = nextId();

            // Type bit 3 = spinner; bit 2 = new combo (spinners always start a new combo in osu!).
            const int type = 0b1100;
            // x,y,time,type,hitSound,endTime,hitSample
            string raw = $"{cx},{cy},{start},{type},{pendingHitSound},{end},{pendingSampleField()}";

            pushUndo();
            removeObjectsAt(start);
            parsed.HitObjects.Add(new HitObjectModel(cx, cy, start, HitObjectKind.Spinner, null,
                Duration: Math.Max(0, end - start), RawLine: raw, Id: id,
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank,
                SampleVolume: pendingSampleVolume, SampleIndex: pendingSampleIndex));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            // Leave it unselected so the tool stays armed for the next spinner (see PlaceCircle).
            selection.Clear();
        }

        /// <summary>
        /// Rebuilds a slider from edited control points (move / add / delete / type toggle). Like lazer, the
        /// slider length follows the new control polygon, snapped so its tail still lands on the beat grid;
        /// existing per-segment types are preserved.
        /// </summary>
        public void UpdateSliderAnchors(int id, IReadOnlyList<SliderControlPoint> points)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return;

            var o = parsed.HitObjects[idx];
            var cps = clampControlPoints(points);
            if (cps.Count < 2)
                return;

            // The first control point must always carry a definite type (lazer invariant).
            if (cps[0].Type == null)
                cps[0] = cps[0] with { Type = SliderPathType.Bezier };

            // The slider follows its anchors (its length is the full control-polygon curve length, so moving
            // any node resizes it), then snaps so the tail always lands on a beat-snap tick.
            double pixelLength = snapSliderLength(o.StartTime, SliderGeometry.PathLength(SliderGeometry.ComputePath(cps)));
            var path = SliderGeometry.ComputePath(cps, pixelLength);
            if (path.Count < 2 || pixelLength < 1)
                return;

            string raw = HitObjectLineEditor.SetSliderCurve(o.RawLine, cps, pixelLength);
            double duration = sliderDuration(o.StartTime, pixelLength, o.Slides);

            pushUndo();
            parsed.HitObjects[idx] = o with
            {
                X = (int)Math.Round(cps[0].X),
                Y = (int)Math.Round(cps[0].Y),
                Path = path,
                ControlPoints = cps,
                Duration = duration,
                RawLine = raw,
            };
            afterEdit();
            selection.SetSingle(id);
        }

        /// <summary>The tick-snapped path for a slider's anchors (head time), so the live reshape preview matches the commit.</summary>
        public IReadOnlyList<Vector2> SnappedSliderPath(int id, IReadOnlyList<SliderControlPoint> points)
        {
            var cps = clampControlPoints(points);
            if (cps.Count < 2)
                return System.Array.Empty<Vector2>();

            if (cps[0].Type == null)
                cps[0] = cps[0] with { Type = SliderPathType.Bezier };

            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            double startTime = idx >= 0 ? parsed.HitObjects[idx].StartTime : snapTime(CurrentTime);

            double pixelLength = snapSliderLength(startTime, SliderGeometry.PathLength(SliderGeometry.ComputePath(cps)));
            return SliderGeometry.ComputePath(cps, pixelLength);
        }

        /// <summary>The finalized path a freshly-placed slider would take (same type-inference + tick snap as <see cref="PlaceSlider"/>), so the live placement preview renders the actual slider body that will be committed.</summary>
        public IReadOnlyList<Vector2> PlacementSliderPath(IReadOnlyList<SliderControlPoint> points)
        {
            var cps = SliderGeometry.InferSegmentTypes(clampControlPoints(points));
            if (cps.Count < 2)
                return System.Array.Empty<Vector2>();

            int time = placementTime();
            double drawn = SliderGeometry.PathLength(SliderGeometry.ComputePath(cps));
            if (drawn < placementMinLength(time))
                return System.Array.Empty<Vector2>(); // not yet a tick long - render nothing (lazer)

            double pixelLength = snapSliderLength(time, drawn);
            if (pixelLength < 1)
                return System.Array.Empty<Vector2>();

            return SliderGeometry.ComputePath(cps, pixelLength);
        }

        /// <summary>The duration a freshly-placed slider would have (same snap as <see cref="PlaceSlider"/>), for the live placement follow points.</summary>
        public double PlacementSliderDuration(IReadOnlyList<SliderControlPoint> points)
        {
            var cps = SliderGeometry.InferSegmentTypes(clampControlPoints(points));
            if (cps.Count < 2)
                return 0;

            int time = placementTime();
            double drawn = SliderGeometry.PathLength(SliderGeometry.ComputePath(cps));
            if (drawn < placementMinLength(time))
                return 0;

            double pixelLength = snapSliderLength(time, drawn);
            if (pixelLength < 1)
                return 0;

            return sliderDuration(time, pixelLength, 1);
        }


        public void PreviewSliderPlacement(IReadOnlyList<SliderControlPoint> points)
        {
            previewSliderTimeline(placementTime(), points, snap: true);
        }

        public void PreviewSliderResize(int id, IReadOnlyList<SliderControlPoint> points)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0)
                return;

            // Reshaping snaps the tail to a tick (matching the commit), so the preview matches the result.
            previewSliderTimeline(parsed.HitObjects[idx].StartTime, points, snap: true);
        }

        public void ClearSliderPreview() => topTimeline.ClearSliderPreview();

        /// <summary>Computes the length/duration a slider would have and shows its extent on the timeline.</summary>
        private void previewSliderTimeline(double startTime, IReadOnlyList<SliderControlPoint> points, bool snap)
        {
            var cps = SliderGeometry.InferSegmentTypes(clampControlPoints(points));
            if (cps.Count < 2)
            {
                topTimeline.ClearSliderPreview();
                return;
            }

            double fullLength = SliderGeometry.PathLength(SliderGeometry.ComputePath(cps));
            double pixelLength = snap ? snapSliderLength(startTime, fullLength) : fullLength;
            if (pixelLength < 1)
            {
                topTimeline.ClearSliderPreview();
                return;
            }

            double duration = sliderDuration(startTime, pixelLength, 1);
            topTimeline.ShowSliderPreview(startTime, duration, beatLengthAt(startTime));
        }

        /// <summary>
        /// Snaps a slider's pixel length so its tail falls on the active beat-snap tick: rounds the resulting
        /// travel time to a whole number of 1/divisor beats, then converts back to a length (lazer-style).
        /// </summary>
        private double snapSliderLength(double time, double pixelLength)
        {
            double beatLength = beatLengthAt(time);
            double sv = velocityAt(time);
            double mult = parsed.SliderMultiplier;
            if (mult <= 0 || beatLength <= 0)
                return pixelLength;

            double velocity = mult * 100 * sv / beatLength;          // osu!px per ms
            double tickMs = beatLength / beatDivisor.Value.Value;     // one beat-snap division
            double duration = pixelLength / velocity;
            double snapped = Math.Round(duration / tickMs) * tickMs;

            // Never snap PAST the drawn curve. Extending the expected distance beyond the path length makes the
            // tail shoot off in a straight line (lazer's calculateLength extends linearly). Lazer's
            // FindSnappedDistance *always rounds down* for exactly this reason, so step down a tick whenever the
            // nearest tick would overshoot the drawn curve.
            if (velocity * snapped > pixelLength)
                snapped -= tickMs;

            // If even a single division still overshoots (the drawn curve is shorter than one tick of travel),
            // keep the raw drawn length rather than extending it into a straight stub.
            if (snapped < tickMs)
                return pixelLength;

            return velocity * snapped;
        }

        /// <summary>Clamps each control point to the playfield in integer osu!pixels (preserving its segment type).</summary>
        private static List<SliderControlPoint> clampControlPoints(IReadOnlyList<SliderControlPoint> points)
        {
            var result = new List<SliderControlPoint>(points.Count);
            foreach (var p in points)
            {
                result.Add(p with
                {
                    X = Math.Clamp((int)Math.Round(p.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH),
                    Y = Math.Clamp((int)Math.Round(p.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT),
                });
            }
            return result;
        }

        /// <summary>Removes any existing objects sharing the given start time (lazer replaces same-tick objects on placement).</summary>
        private void removeObjectsAt(int time)
        {
            for (int i = parsed.HitObjects.Count - 1; i >= 0; i--)
            {
                if (parsed.HitObjects[i].StartTime == time)
                {
                    selection.Deselect(parsed.HitObjects[i].Id);
                    parsed.HitObjects.RemoveAt(i);
                }
            }
        }

        /// <summary>The next free stable object id (one past the current maximum).</summary>
        private int nextId() => parsed.HitObjects.Count == 0 ? 0 : parsed.HitObjects.Max(o => o.Id) + 1;

        /// <summary>The uninherited beat length (ms) in effect at the given time.</summary>
        private double beatLengthAt(double time)
        {
            double beatLength = parsed.BeatPoints.Count > 0 ? parsed.BeatPoints[0].BeatLength : 500;
            foreach (var p in parsed.BeatPoints)
            {
                if (p.Time <= time)
                    beatLength = p.BeatLength;
                else
                    break;
            }
            return beatLength;
        }

        /// <summary>The effective slider-velocity multiplier (green-line SV) in force at the given time.</summary>
        private double velocityAt(double time)
        {
            // Binary search the time-sorted green lines for the last one at/before `time`. This runs every
            // frame (BPM/SV readouts) and on every slider length calc, so on SV-heavy maps the old linear scan
            // was O(points) per call; this is O(log points) for identical results.
            var pts = parsed.VelocityPoints;
            int lo = 0, hi = pts.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (pts[mid].Time <= time)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo > 0 ? pts[lo - 1].Multiplier : 1;
        }

        /// <summary>
        /// The SV in force <b>strictly before</b> the given time, ignoring any green line exactly at it - i.e. the
        /// speed the map runs at going into a slider, which a velocity edit restores in a reset line just past it.
        /// </summary>
        private double velocityBefore(double time)
        {
            double sv = 1;
            int t = (int)Math.Round(time);
            foreach (var p in parsed.VelocityPoints)
            {
                if ((int)Math.Round(p.Time) < t)
                    sv = p.Multiplier;
                else
                    break;
            }
            return sv;
        }

        /// <summary>The latest tail time among the objects keyed in <paramref name="velocityById"/> (a pasted pattern).</summary>
        private double patternEnd(IReadOnlyDictionary<int, double> velocityById)
            => parsed.HitObjects.Where(o => velocityById.ContainsKey(o.Id)).Max(o => o.StartTime + o.Duration);

        /// <summary>
        /// The hitsound volume (0..1) active at the given time, from the timing model's sample points (both red
        /// and green carry a volume). The last point at-or-before the time wins, and on a tie the later one in
        /// the list wins (matching osu!), so a green point silences exactly from its time onward.
        /// </summary>
        private float volumeAt(double time)
        {
            int volume = 100;
            double bestTime = double.NegativeInfinity;
            foreach (var p in parsed.TimingPointModels)
            {
                if (p.Time <= time && p.Time >= bestTime)
                {
                    bestTime = p.Time;
                    volume = p.Volume;
                }
            }
            return Math.Clamp(volume / 100f, 0f, 1f);
        }

        /// <summary>The concrete sample bank the timing model is in force at the given time (its sample set; default Normal).</summary>
        private SampleBank sampleSetAt(double time)
        {
            int set = 0;
            double bestTime = double.NegativeInfinity;
            foreach (var p in parsed.TimingPointModels)
            {
                if (p.Time <= time && p.Time >= bestTime)
                {
                    bestTime = p.Time;
                    set = p.SampleSet;
                }
            }
            return set switch { 2 => SampleBank.Soft, 3 => SampleBank.Drum, _ => SampleBank.Normal };
        }

        /// <summary>The custom sample-bank index of the timing point active at <paramref name="time"/> (0 if none).</summary>
        private int sampleIndexAt(double time)
        {
            int index = 0;
            double bestTime = double.NegativeInfinity;
            foreach (var p in parsed.TimingPointModels)
            {
                if (p.Time <= time && p.Time >= bestTime)
                {
                    bestTime = p.Time;
                    index = p.SampleIndex;
                }
            }
            return index;
        }

        /// <summary>An object's effective sample index: its own override when set (&gt;0), else the active timing point's (osu! inherit).</summary>
        private int effectiveIndex(int objectIndex, double time) => objectIndex > 0 ? objectIndex : sampleIndexAt(time);

        /// <summary>
        /// Resolves an object's (possibly <see cref="SampleBank.Auto"/>) normal/addition banks to concrete banks at
        /// the given time, following osu!'s inherit chain: a normal Auto takes the timing point's set, and an
        /// addition Auto takes the resolved normal. So an "Auto" bank tracks the timing points, like in osu!.
        /// </summary>
        private (SampleBank Normal, SampleBank Addition) resolveBanksAt(double time, SampleBank normal, SampleBank addition)
        {
            SampleBank n = normal == SampleBank.Auto ? sampleSetAt(time) : normal;
            SampleBank a = addition == SampleBank.Auto ? n : addition;
            return (n, a);
        }

        /// <summary>Travel time (ms) of a slider of the given pixel length and span count, honouring BPM and green-line SV.</summary>
        private double sliderDuration(double time, double pixelLength, int slides)
        {
            double beatLength = beatLengthAt(time);
            double sv = velocityAt(time);
            double mult = parsed.SliderMultiplier;
            double span = mult > 0 ? pixelLength * beatLength / (mult * 100 * sv) : 0;

            // Duration follows the (already tick-snapped) pixel length exactly - no artificial floor. A fixed
            // minimum (the old Math.Max(60, ...)) inflated short sliders past their tick: a 1/8 tick at >125 BPM,
            // or 1/6 at >166 BPM, is shorter than 60ms, so the tail always overshot the beat-snap tick. The
            // pixel length is already gated to >=1 tick at placement, so span is naturally positive; the 60ms
            // fallback only guards the degenerate mult<=0 case.
            double total = span * slides;
            return total > 0 ? total : 60;
        }

        /// <summary>
        /// Toggles "new combo" - on the selected objects if any are selected, otherwise arming it for the
        /// next placed circle (the placement preview reflects it).
        /// </summary>
        private void toggleNewCombo()
        {
            if (selection.Selected.Count > 0)
            {
                pushUndo();
                var ids = new HashSet<int>(selection.Selected);

                for (int i = 0; i < parsed.HitObjects.Count; i++)
                {
                    if (ids.Contains(parsed.HitObjects[i].Id))
                        parsed.HitObjects[i] = parsed.HitObjects[i] with { RawLine = HitObjectLineEditor.ToggleNewCombo(parsed.HitObjects[i].RawLine) };
                }

                afterEdit();
            }
            else
            {
                playfield.NewComboArmed = !playfield.NewComboArmed;
            }
        }

        // --- Hitsound editing (left-panel palette): additions + sample banks on the selection, or pending defaults ---

        /// <summary>A snapshot of the hitsounds shown by the palette: the selection's, or the pending placement defaults.
        /// <see cref="Volume"/>/<see cref="Index"/> are object-level (0 = inherit the timing point / "Auto").</summary>
        public readonly record struct HitsoundState(int HitSound, SampleBank Normal, SampleBank Addition, bool HasSelection, float Volume = 0f, int Index = 0);

        /// <summary>The hitsounds the palette should display: the selected slider node's, else the first selected object's, else the pending defaults.</summary>
        public HitsoundState CurrentHitsoundState()
        {
            // Volume/index are object-level (not per-node), so a selected node reports its owning object's volume/index.
            if (nodeSelection.Selected is { } node && tryNodeSample(node, out NodeSample ns))
            {
                int oi = parsed.HitObjects.FindIndex(x => x.Id == node.ObjectId);
                var owner = oi >= 0 ? parsed.HitObjects[oi] : default;
                return new HitsoundState(ns.HitSound, ns.NormalBank, ns.AdditionBank, true, owner.SampleVolume, owner.SampleIndex);
            }

            foreach (var o in parsed.HitObjects)
            {
                if (selection.Contains(o.Id))
                    return new HitsoundState(o.HitSound, o.NormalBank, o.AdditionBank, true, o.SampleVolume, o.SampleIndex);
            }

            return new HitsoundState(pendingHitSound, pendingNormalBank, pendingAdditionBank, false, pendingSampleVolume, pendingSampleIndex);
        }

        /// <summary>Reads the sample for a selected slider node, normalising the node list to <c>Slides + 1</c> entries.</summary>
        private bool tryNodeSample((int ObjectId, int NodeIndex) node, out NodeSample sample)
        {
            sample = default;
            int idx = parsed.HitObjects.FindIndex(o => o.Id == node.ObjectId);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return false;

            var nodes = ensureNodeSamples(parsed.HitObjects[idx]);
            if (node.NodeIndex < 0 || node.NodeIndex >= nodes.Count)
                return false;

            sample = nodes[node.NodeIndex];
            return true;
        }

        /// <summary>A slider's per-node samples as an editable list of length <c>Slides + 1</c>, filling gaps from the object banks.</summary>
        private static List<NodeSample> ensureNodeSamples(HitObjectModel o)
        {
            int count = Math.Max(1, o.Slides) + 1;
            var list = new List<NodeSample>(count);
            for (int i = 0; i < count; i++)
            {
                if (o.NodeSamples != null && i < o.NodeSamples.Count)
                    list.Add(o.NodeSamples[i]);
                else
                    list.Add(new NodeSample(o.HitSound, o.NormalBank, o.AdditionBank));
            }
            return list;
        }

        /// <summary>Mutates the selected slider node's sample as one undo step, then plays it. Returns false if there is no node selection.</summary>
        private bool applyNodeEdit(Func<NodeSample, NodeSample> mutate)
        {
            if (nodeSelection.Selected is not { } node)
                return false;

            int idx = parsed.HitObjects.FindIndex(o => o.Id == node.ObjectId);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return false;

            var o = parsed.HitObjects[idx];
            var nodes = ensureNodeSamples(o);
            if (node.NodeIndex < 0 || node.NodeIndex >= nodes.Count)
                return false;

            pushUndo();
            nodes[node.NodeIndex] = mutate(nodes[node.NodeIndex]);
            parsed.HitObjects[idx] = o with
            {
                NodeSamples = nodes,
                RawLine = HitObjectLineEditor.SetSliderNodeSamples(o.RawLine, nodes),
            };
            afterEdit();

            var s = nodes[node.NodeIndex];
            playFeedback(o.StartTime, s.HitSound, s.NormalBank, s.AdditionBank);
            return true;
        }

        /// <summary>The pending placement sample set as an "normalSet:additionSet" pair (for slider edgeSets).</summary>
        private string pendingSet() => $"{HitObjectLineEditor.SampleSet(pendingNormalBank)}:{HitObjectLineEditor.SampleSet(pendingAdditionBank)}";

        /// <summary>The pending placement hitSample field: "normalSet:additionSet:index:volume:filename" (0/0 = inherit).</summary>
        private string pendingSampleField() => $"{pendingSet()}:{Math.Max(0, pendingSampleIndex)}:{(pendingSampleVolume > 0 ? Math.Clamp((int)Math.Round(pendingSampleVolume * 100), 1, 100) : 0)}:";

        /// <summary>
        /// Toggles a whistle/finish/clap addition (bit) on the selection (object + every slider node), or on the
        /// pending placement defaults if nothing is selected. Plays the result so the change is audible.
        /// </summary>
        public void ToggleAddition(int bit)
        {
            // A selected slider node takes precedence: toggle the addition on that edge only.
            if (applyNodeEdit(n => n with { HitSound = (n.HitSound & bit) != 0 ? n.HitSound & ~bit : n.HitSound | bit }))
                return;

            if (selection.Selected.Count == 0)
            {
                pendingHitSound ^= bit;
                playFeedback(CurrentTime, pendingHitSound, pendingNormalBank, pendingAdditionBank);
                return;
            }

            // Uniform toggle: if any selected object lacks the bit, set it on all; otherwise clear it on all.
            bool turnOn = parsed.HitObjects.Any(o => selection.Contains(o.Id) && (o.HitSound & bit) == 0);

            applyHitsoundEdit(o =>
            {
                int hs = turnOn ? o.HitSound | bit : o.HitSound & ~bit;
                var nodes = o.NodeSamples?.Select(n => n with { HitSound = turnOn ? n.HitSound | bit : n.HitSound & ~bit }).ToList();
                string raw = HitObjectLineEditor.SetHitSound(o.RawLine, hs);
                if (o.Kind == HitObjectKind.Slider && nodes != null)
                    raw = HitObjectLineEditor.SetSliderNodeSamples(raw, nodes);
                return o with { HitSound = hs, NodeSamples = nodes ?? o.NodeSamples, RawLine = raw };
            });
        }

        /// <summary>Sets the normal sample bank (drives the hitnormal) on the selection, or the pending default.</summary>
        public void SetNormalBank(SampleBank bank)
        {
            if (applyNodeEdit(n => n with { NormalBank = bank }))
                return;

            if (selection.Selected.Count == 0)
            {
                pendingNormalBank = bank;
                playFeedback(CurrentTime, pendingHitSound, pendingNormalBank, pendingAdditionBank);
                return;
            }

            applyHitsoundEdit(o =>
            {
                var nodes = o.NodeSamples?.Select(n => n with { NormalBank = bank }).ToList();
                string raw = HitObjectLineEditor.SetSampleBanks(o.RawLine, bank, o.AdditionBank);
                if (o.Kind == HitObjectKind.Slider && nodes != null)
                    raw = HitObjectLineEditor.SetSliderNodeSamples(raw, nodes);
                return o with { NormalBank = bank, NodeSamples = nodes ?? o.NodeSamples, RawLine = raw };
            });
        }

        /// <summary>Sets the addition sample bank (drives whistle/finish/clap) on the selection, or the pending default.</summary>
        public void SetAdditionBank(SampleBank bank)
        {
            if (applyNodeEdit(n => n with { AdditionBank = bank }))
                return;

            if (selection.Selected.Count == 0)
            {
                pendingAdditionBank = bank;
                playFeedback(CurrentTime, pendingHitSound, pendingNormalBank, pendingAdditionBank);
                return;
            }

            applyHitsoundEdit(o =>
            {
                var nodes = o.NodeSamples?.Select(n => n with { AdditionBank = bank }).ToList();
                string raw = HitObjectLineEditor.SetSampleBanks(o.RawLine, o.NormalBank, bank);
                if (o.Kind == HitObjectKind.Slider && nodes != null)
                    raw = HitObjectLineEditor.SetSliderNodeSamples(raw, nodes);
                return o with { AdditionBank = bank, NodeSamples = nodes ?? o.NodeSamples, RawLine = raw };
            });
        }

        /// <summary>Applies a per-object hitsound mutation to every selected object as one undo step, then plays a sample.</summary>
        private void applyHitsoundEdit(Func<HitObjectModel, HitObjectModel> mutate)
        {
            pushUndo();

            HitObjectModel? sample = null;
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                if (!selection.Contains(parsed.HitObjects[i].Id))
                    continue;

                parsed.HitObjects[i] = mutate(parsed.HitObjects[i]);
                sample ??= parsed.HitObjects[i];
            }

            afterEdit();

            if (sample is { } s)
                playFeedback(s.StartTime, s.HitSound, s.NormalBank, s.AdditionBank);
        }

        /// <summary>
        /// Applies an OBJECT-LEVEL hitsound mutation (volume / index — fields with no per-node form) to the target
        /// objects as one undo step: the selected objects, or the object owning a selected slider node. Returns false
        /// (and changes nothing) when nothing is targeted, so the caller can fall back to the pending defaults.
        /// </summary>
        private bool applyObjectLevelHitsound(Func<HitObjectModel, HitObjectModel> mutate)
        {
            var ids = new HashSet<int>(selection.Selected);
            if (ids.Count == 0 && nodeSelection.Selected is { } node)
                ids.Add(node.ObjectId);
            if (ids.Count == 0)
                return false;

            pushUndo();

            HitObjectModel? sample = null;
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                if (!ids.Contains(parsed.HitObjects[i].Id))
                    continue;

                parsed.HitObjects[i] = mutate(parsed.HitObjects[i]);
                sample ??= parsed.HitObjects[i];
            }

            afterEdit();

            if (sample is { } s)
                playFeedback(s.StartTime, s.HitSound, s.NormalBank, s.AdditionBank);
            return true;
        }

        /// <summary>Sets the sample volume override on the selection/node-owner, or the pending default. 0 = inherit the timing point ("Auto").</summary>
        public void SetSampleVolume(float volume)
        {
            int pct = volume <= 0 ? 0 : Math.Clamp((int)Math.Round(volume * 100), 1, 100);

            if (applyObjectLevelHitsound(o => o with { SampleVolume = pct / 100f, RawLine = HitObjectLineEditor.SetSampleVolume(o.RawLine, pct) }))
                return;

            pendingSampleVolume = pct / 100f;
            playFeedback(CurrentTime, pendingHitSound, pendingNormalBank, pendingAdditionBank);
        }

        /// <summary>Sets the custom sample-bank index on the selection/node-owner, or the pending default. 0 = inherit the timing point ("Auto").</summary>
        public void SetSampleIndex(int index)
        {
            index = Math.Max(0, index);

            if (applyObjectLevelHitsound(o => o with { SampleIndex = index, RawLine = HitObjectLineEditor.SetSampleIndex(o.RawLine, index) }))
                return;

            pendingSampleIndex = index;
            playFeedback(CurrentTime, pendingHitSound, pendingNormalBank, pendingAdditionBank);
        }

        /// <summary>A copied hitsound "feel": the object-level additions/banks/volume/index plus the per-node samples (for sliders).</summary>
        public readonly record struct HitsoundClip(int HitSound, SampleBank Normal, SampleBank Addition, float Volume, int Index, IReadOnlyList<NodeSample>? Nodes);

        /// <summary>Whether there is a hitsound clip ready to paste (drives the Paste button's enabled state).</summary>
        public bool HasHitsoundClip => hitsoundClipboard != null;

        /// <summary>Copies the first selected object's hitsounds (additions, banks, volume, index, per-node samples) for later paste.</summary>
        public void CopyHitsounds()
        {
            HitObjectModel? src = null;
            foreach (var o in parsed.HitObjects)
                if (selection.Contains(o.Id)) { src = o; break; }

            // Fall back to the object owning a selected slider node.
            if (src == null && nodeSelection.Selected is { } node)
            {
                int i = parsed.HitObjects.FindIndex(x => x.Id == node.ObjectId);
                if (i >= 0) src = parsed.HitObjects[i];
            }

            if (src is { } s)
            {
                hitsoundClipboard = new HitsoundClip(s.HitSound, s.NormalBank, s.AdditionBank, s.SampleVolume, s.SampleIndex, s.NodeSamples);
                toasts?.Push("Hitsounds copied", EditorTheme.Colours.Velocity);
            }
        }

        /// <summary>Pastes the copied hitsounds onto every selected object (one undo step); per-node samples map by node, clamped to each target.</summary>
        public void PasteHitsounds()
        {
            if (hitsoundClipboard is not { } clip || selection.Selected.Count == 0)
                return;

            int pct = clip.Volume > 0 ? Math.Clamp((int)Math.Round(clip.Volume * 100), 1, 100) : 0;

            applyHitsoundEdit(o =>
            {
                // Per-node samples: reuse the clip's node list, clamped/extended to this slider's own node count.
                List<NodeSample>? nodes = null;
                if (o.Kind == HitObjectKind.Slider)
                {
                    int count = Math.Max(1, o.Slides) + 1;
                    nodes = new List<NodeSample>(count);
                    for (int i = 0; i < count; i++)
                    {
                        NodeSample ns = clip.Nodes is { Count: > 0 }
                            ? clip.Nodes[Math.Min(i, clip.Nodes.Count - 1)]
                            : new NodeSample(clip.HitSound, clip.Normal, clip.Addition);
                        nodes.Add(ns);
                    }
                }

                string raw = HitObjectLineEditor.SetHitSound(o.RawLine, clip.HitSound);
                raw = HitObjectLineEditor.SetSampleBanks(raw, clip.Normal, clip.Addition);
                raw = HitObjectLineEditor.SetSampleIndex(raw, clip.Index);
                raw = HitObjectLineEditor.SetSampleVolume(raw, pct);
                if (o.Kind == HitObjectKind.Slider && nodes != null)
                    raw = HitObjectLineEditor.SetSliderNodeSamples(raw, nodes);

                return o with
                {
                    HitSound = clip.HitSound,
                    NormalBank = clip.Normal,
                    AdditionBank = clip.Addition,
                    SampleVolume = pct / 100f,
                    SampleIndex = clip.Index,
                    NodeSamples = nodes ?? o.NodeSamples,
                    RawLine = raw,
                };
            });
        }

        /// <summary>Plays a one-off hitsound so a palette change is immediately audible (osu!lazer feedback). Banks
        /// are resolved (Auto -> the timing point at <paramref name="time"/>) so the feedback matches playback.</summary>
        private void playFeedback(double time, int hitSound, SampleBank normal, SampleBank addition)
        {
            var (n, a) = resolveBanksAt(time, normal, addition);
            // Edit feedback honours the active timing point's custom sample index (e.g. soft-hitclap2).
            hitsounds?.Play(hitSound, n, a, 1f, sampleIndexAt(time));
        }

        /// <summary>
        /// Hitsound-lanes edit: sets/clears an addition bit on one object (<paramref name="nodeIndex"/> &lt; 0)
        /// or a single slider node. <paramref name="pushUndoStep"/> = false folds into the prior step so a paint
        /// drag is one undo. The slider's per-node samples drive its edge sounds (see <see cref="rebuildSampleEvents"/>).
        /// </summary>
        public void SetHitsoundAddition(int objectId, int nodeIndex, int bit, bool on, bool pushUndoStep)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == objectId);
            if (idx < 0)
                return;

            var o = parsed.HitObjects[idx];

            if (pushUndoStep)
                pushUndo();

            if (o.Kind == HitObjectKind.Slider && nodeIndex >= 0)
            {
                var nodes = ensureNodeSamples(o);
                if (nodeIndex >= nodes.Count)
                    return;

                var n = nodes[nodeIndex];
                nodes[nodeIndex] = n with { HitSound = on ? n.HitSound | bit : n.HitSound & ~bit };
                parsed.HitObjects[idx] = o with
                {
                    NodeSamples = nodes,
                    RawLine = HitObjectLineEditor.SetSliderNodeSamples(o.RawLine, nodes),
                };

                var s = nodes[nodeIndex];
                playFeedback(o.StartTime, s.HitSound, s.NormalBank, s.AdditionBank);
            }
            else
            {
                int hs = on ? o.HitSound | bit : o.HitSound & ~bit;
                parsed.HitObjects[idx] = o with { HitSound = hs, RawLine = HitObjectLineEditor.SetHitSound(o.RawLine, hs) };
                playFeedback(o.StartTime, hs, o.NormalBank, o.AdditionBank);
            }

            afterEdit();
        }

        /// <summary>Hitsound-lanes edit: sets one object's (or slider node's) normal or addition sample bank.</summary>
        public void SetHitsoundBank(int objectId, int nodeIndex, bool addition, SampleBank bank)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == objectId);
            if (idx < 0)
                return;

            var o = parsed.HitObjects[idx];

            pushUndo();

            if (o.Kind == HitObjectKind.Slider && nodeIndex >= 0)
            {
                var nodes = ensureNodeSamples(o);
                if (nodeIndex >= nodes.Count)
                    return;

                var n = nodes[nodeIndex];
                nodes[nodeIndex] = addition ? n with { AdditionBank = bank } : n with { NormalBank = bank };
                parsed.HitObjects[idx] = o with
                {
                    NodeSamples = nodes,
                    RawLine = HitObjectLineEditor.SetSliderNodeSamples(o.RawLine, nodes),
                };

                var s = nodes[nodeIndex];
                playFeedback(o.StartTime, s.HitSound, s.NormalBank, s.AdditionBank);
            }
            else
            {
                SampleBank normal = addition ? o.NormalBank : bank;
                SampleBank add = addition ? bank : o.AdditionBank;
                parsed.HitObjects[idx] = o with
                {
                    NormalBank = normal,
                    AdditionBank = add,
                    RawLine = HitObjectLineEditor.SetSampleBanks(o.RawLine, normal, add),
                };
                playFeedback(o.StartTime, o.HitSound, normal, add);
            }

            afterEdit();
        }

        public void DeleteSelected()
        {
            if (selection.Selected.Count == 0)
                return;

            pushUndo();
            var ids = new HashSet<int>(selection.Selected);
            parsed.HitObjects.RemoveAll(o => ids.Contains(o.Id));

            selection.Clear();
            afterEdit();
        }

        public void DeleteObject(int id)
        {
            int index = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (index < 0)
                return;

            pushUndo();
            parsed.HitObjects.RemoveAt(index);
            selection.Deselect(id);
            afterEdit();
        }

        public void BeginMove()
        {
            // Snapshot the selection and its bounds; the drag only previews (translates drawables),
            // then commits to the model on release - this is how lazer keeps moving smooth.
            moveSnapshot.Clear();
            moveTimeDelta = 0;
            movePosDelta = Vector2.Zero;

            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);

            foreach (var o in parsed.HitObjects)
            {
                if (!selection.Contains(o.Id))
                    continue;

                moveSnapshot[o.Id] = o;

                if (o.Kind == HitObjectKind.Spinner)
                    continue;

                accumulateBounds(o, ref min, ref max);
            }

            moveMin = min;
            moveMax = max;
        }

        public void MoveSelectionTime(double rawDeltaMs, int grabbedId)
        {
            if (!moveSnapshot.TryGetValue(grabbedId, out var grabbed))
                return;

            // Snap the grabbed object's new time to the beat grid; move the rest by the same delta.
            double delta = snapTime(grabbed.StartTime + rawDeltaMs) - grabbed.StartTime;

            double minStart = moveSnapshot.Values.Min(m => m.StartTime);
            if (minStart + delta < 0)
                delta = -minStart;

            moveTimeDelta = (int)Math.Round(delta);
            topTimeline.PreviewTimeOffset(moveTimeDelta);
        }

        public void MoveSelectionPosition(Vector2 rawDelta)
        {
            if (moveSnapshot.Count == 0 || moveMin.X > moveMax.X)
                return; // nothing movable (e.g. spinner-only selection)

            Vector2 delta = new Vector2((float)Math.Round(rawDelta.X), (float)Math.Round(rawDelta.Y));

            // Magnetic stacking snap: if dragging the selection brings any of its snap points within the
            // magnetic radius of an unselected on-screen object, latch the two together. This mirrors lazer's
            // checkSnappingBlueprintToNearbyObjects + snapToVisibleBlueprints - it lets you drop an object onto
            // another to stack them precisely, no matter how far apart in time (the stacking below then resolves
            // the diagonal offset if they're within stack leniency).
            delta = magneticMoveSnap(delta);

            // Clamp so the whole selection stays inside the playfield.
            float dx = Math.Clamp(delta.X, -moveMin.X, ParsedBeatmap.PLAYFIELD_WIDTH - moveMax.X);
            float dy = Math.Clamp(delta.Y, -moveMin.Y, ParsedBeatmap.PLAYFIELD_HEIGHT - moveMax.Y);

            movePosDelta = new Vector2(dx, dy);

            // Recompute stacking against the dragged positions so the selection visibly stacks onto objects it
            // overlaps - the same algorithm lazer's editor runs live while you move objects.
            playfield.PreviewPositionOffset(movePosDelta, liveStackHeights(movePosDelta));
        }

        /// <summary>
        /// Resolves where a placement at the cursor should actually land: distance snap (if enabled) spaces it
        /// from the previous object by the time gap, then magnetic snapping locks onto a very close object.
        /// Used both by the placement preview and by the actual placement so they always agree.
        /// </summary>
        public Vector2 SnapPlacement(Vector2 cursor)
        {
            int time = (int)Math.Round(snapTime(CurrentTime));

            Vector2 pos = cursor;
            if (distanceSnapEnabled)
                pos = applyDistanceSnap(pos, time);

            // Magnetic snap to nearby visible objects, exactly as osu!lazer's snapToVisibleBlueprints: the
            // radius is OsuHitObject.OBJECT_RADIUS * 0.10 (a fixed 6.4 osu!px, independent of circle size),
            // and only on-screen, unselected objects are considered.
            const float magnetic_snap_radius = OsuObjectRadius * 0.10f;
            if (tryNearestObjectPosition(pos, o => !playfield.IsObjectVisible(o) || selection.Contains(o.Id), out Vector2 target, out float dist) && dist < magnetic_snap_radius)
                pos = target;

            return pos;
        }

        /// <summary>Constrains a placement position to a distance from the previous object equal to its time gap times the slider velocity.</summary>
        private Vector2 applyDistanceSnap(Vector2 cursor, double time)
        {
            var prev = previousObject(time);
            if (prev == null)
                return cursor;

            var p = prev.Value;
            Vector2 prevEnd = p.Kind == HitObjectKind.Slider && p.Path is { Count: > 0 } path ? path[^1] : new Vector2(p.X, p.Y);
            double prevEndTime = p.StartTime + (p.Kind == HitObjectKind.Slider ? p.Duration : 0);

            double dt = time - prevEndTime;
            if (dt <= 0)
                return cursor;

            double velocity = pixelVelocityAt(prevEndTime); // osu!px per ms
            double distance = distanceSpacing * velocity * dt;

            Vector2 dir = cursor - prevEnd;
            if (dir.LengthSquared < 1e-3f)
                dir = new Vector2(1, 0);
            dir = Vector2.Normalize(dir);

            return prevEnd + dir * (float)distance;
        }

        /// <summary>The slider-velocity in osu!pixels per millisecond at the given time.</summary>
        private double pixelVelocityAt(double time)
        {
            double beatLength = beatLengthAt(time);
            return beatLength > 0 ? parsed.SliderMultiplier * 100 * velocityAt(time) / beatLength : 0;
        }

        /// <summary>The last hit object that starts strictly before the given time, or null.</summary>
        private HitObjectModel? previousObject(double time)
        {
            HitObjectModel? prev = null;
            foreach (var o in parsed.HitObjects)
            {
                if (o.StartTime < time)
                    prev = o;
                else
                    break;
            }
            return prev;
        }

        /// <summary>osu!lazer's <c>OsuHitObject.OBJECT_RADIUS</c> constant (the base gamefield radius).</summary>
        private const float OsuObjectRadius = 64f;

        /// <summary>
        /// Finds the nearest non-excluded object snap point to a position, returning false if none exist. The
        /// snap points per object mirror osu!lazer's selection blueprints: a circle's centre; a slider's head,
        /// its geometric path end, and the control-point anchors that produce visible kinks; a spinner's centre.
        /// </summary>
        private bool tryNearestObjectPosition(Vector2 pos, Func<HitObjectModel, bool> isExcluded, out Vector2 nearest, out float distance)
        {
            nearest = pos;
            distance = float.MaxValue;
            bool found = false;

            foreach (var o in parsed.HitObjects)
            {
                if (isExcluded(o))
                    continue;

                foreach (var candidate in snapPointsFor(o))
                    considerCandidate(candidate, pos, ref nearest, ref distance, ref found);
            }

            return found;
        }

        /// <summary>
        /// The osu!pixel snap points of an object, mirroring its osu!lazer selection blueprint's
        /// <c>ScreenSpaceSnapPoints</c> (unstacked positions, as lazer snaps to the unstacked target).
        /// </summary>
        private static IEnumerable<Vector2> snapPointsFor(HitObjectModel o)
        {
            if (o.Kind == HitObjectKind.Spinner)
            {
                // SpinnerSelectionBlueprint uses the default selection point: the draw quad centre (playfield centre).
                yield return new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f);
                yield break;
            }

            // Head: a slider's first path point, else the object position.
            yield return o.Path is { Count: > 0 } hp ? hp[0] : new Vector2(o.X, o.Y);

            if (o.Kind != HitObjectKind.Slider || o.Path is not { Count: > 0 } path)
                yield break;

            // Geometric path end (lazer's PathEndOffset, used regardless of repeat count).
            yield return path[^1];

            // Control points that produce visible kinks (getScreenSpaceControlPointNodes): segment-start
            // anchors, plus all points on a linear segment; the head (i==0) and final point are excluded.
            if (o.ControlPoints is not { Count: > 1 } cps)
                yield break;

            SliderPathType? currentType = null;
            for (int i = 0; i < cps.Count - 1; i++)
            {
                var cp = cps[i];
                if (cp.Type != null)
                    currentType = cp.Type;

                if (i == 0)
                    continue;

                if (cp.Type == null && currentType?.Type != SliderSplineType.Linear)
                    continue;

                yield return cp.Position;
            }
        }

        private static void considerCandidate(Vector2 candidate, Vector2 pos, ref Vector2 nearest, ref float distance, ref bool found)
        {
            float d = (candidate - pos).Length;
            if (d < distance)
            {
                distance = d;
                nearest = candidate;
                found = true;
            }
        }

        /// <summary>
        /// Mirrors lazer's <c>OsuBlueprintContainer.checkSnappingBlueprintToNearbyObjects</c>: tests each
        /// selected object's snap points (at their original positions, shifted by <paramref name="delta"/>) and,
        /// if one lands within the magnetic radius of an unselected, on-screen object's (unstacked) snap point,
        /// returns the adjusted delta that latches them together. Otherwise returns the delta unchanged.
        /// </summary>
        private Vector2 magneticMoveSnap(Vector2 delta)
        {
            const float magnetic_snap_radius = OsuObjectRadius * 0.10f;

            foreach (var snap in moveSnapshot.Values)
            {
                if (snap.Kind == HitObjectKind.Spinner)
                    continue;

                foreach (var sp in snapPointsFor(snap))
                {
                    Vector2 testPos = sp + delta;
                    if (tryNearestObjectPosition(testPos, o => !playfield.IsObjectVisible(o) || selection.Contains(o.Id), out Vector2 target, out float dist)
                        && dist < magnetic_snap_radius)
                        return target - sp;
                }
            }

            return delta;
        }

        /// <summary>Stack heights the objects would take if the selection were dropped at the current move offset.</summary>
        private Dictionary<int, int> liveStackHeights(Vector2 delta)
        {
            var temp = new List<HitObjectModel>(parsed.HitObjects.Count);
            foreach (var o in parsed.HitObjects)
            {
                if (moveSnapshot.ContainsKey(o.Id) && o.Kind != HitObjectKind.Spinner)
                    temp.Add(o with
                    {
                        X = o.X + delta.X,
                        Y = o.Y + delta.Y,
                        Path = offsetPath(o.Path, (int)delta.X, (int)delta.Y),
                    });
                else
                    temp.Add(o);
            }

            StackingProcessor.Apply(temp, ParsedBeatmap.PreemptFor(editable.Ar.Value), parsed.StackLeniency);

            var heights = new Dictionary<int, int>(temp.Count);
            foreach (var o in temp)
                heights[o.Id] = o.StackHeight;
            return heights;
        }

        /// <summary>The original slider being converted while the slider-to-stream panel is open (preview state).</summary>
        private HitObjectModel? streamOriginal;

        /// <summary>The ids of the live-preview circles currently standing in for <see cref="streamOriginal"/>.</summary>
        private readonly List<int> streamPreviewIds = new List<int>();

        /// <summary>
        /// Opens the slider-to-stream panel for the single selected slider, seeding it with the beat-snap tick
        /// count (what osu!stable's "convert slider to stream" would use). While the panel is open the stream is
        /// previewed live on the playfield via <see cref="refreshStreamPreview"/>; confirm/cancel commit or undo it.
        /// </summary>
        private void convertSelectedSliderToStream()
        {
            if (streamOriginal != null)
                return; // already converting - ignore re-triggers

            if (selection.Selected.Count != 1)
                return;

            int id = selection.Selected.First();
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return;

            var slider = parsed.HitObjects[idx];
            if (slider.Path is not { Count: >= 2 } || slider.Duration <= 0)
                return;

            double step = beatLengthAt(slider.StartTime) / beatDivisor.Value.Value;
            int defaultCount = step > 0 ? Math.Clamp((int)Math.Round(slider.Duration / step) + 1, 2, 128) : 8;

            streamOriginal = slider;
            sliderToStreamOverlay.Show(defaultCount); // fires Preview -> refreshStreamPreview builds the live stream
        }

        /// <summary>Rebuilds the live preview circles for the current panel values (no undo entry).</summary>
        private void refreshStreamPreview(int count, float curve)
        {
            if (streamOriginal == null)
                return;

            var original = streamOriginal.Value;
            parsed.HitObjects.RemoveAll(o => streamPreviewIds.Contains(o.Id) || o.Id == original.Id);
            streamPreviewIds.Clear();

            foreach (var circle in buildStreamCircles(original, count, curve))
            {
                parsed.HitObjects.Add(circle);
                streamPreviewIds.Add(circle.Id);
            }

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            selection.SetRange(streamPreviewIds);
            afterEdit();
        }

        /// <summary>Commits the preview into a single undo-able conversion (Convert button / Enter).</summary>
        private void confirmStreamPreview(int count, float curve)
        {
            if (streamOriginal == null)
                return;

            var original = streamOriginal.Value;

            // Restore the original slider first, then run the conversion through pushUndo so a single Ctrl+Z
            // brings the slider back (the preview mutations themselves never touched the undo stack).
            parsed.HitObjects.RemoveAll(o => streamPreviewIds.Contains(o.Id));
            streamPreviewIds.Clear();
            if (parsed.HitObjects.All(o => o.Id != original.Id))
                parsed.HitObjects.Add(original);
            streamOriginal = null;

            applyStreamConversion(original, count, curve);
        }

        /// <summary>Drops the preview and restores the original slider (Cancel button / Escape / close).</summary>
        private void cancelStreamPreview()
        {
            if (streamOriginal == null)
                return;

            var original = streamOriginal.Value;
            parsed.HitObjects.RemoveAll(o => streamPreviewIds.Contains(o.Id));
            streamPreviewIds.Clear();
            if (parsed.HitObjects.All(o => o.Id != original.Id))
                parsed.HitObjects.Add(original);
            streamOriginal = null;

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            selection.SetRange(new[] { original.Id });
            afterEdit();
        }

        /// <summary>Replaces a slider with a stream of circles as a single undo-able edit.</summary>
        private void applyStreamConversion(HitObjectModel slider, int count, float curve)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == slider.Id);
            if (idx < 0)
                return;

            pushUndo();
            parsed.HitObjects.RemoveAt(idx);
            selection.Clear();

            var added = new List<int>();
            foreach (var circle in buildStreamCircles(slider, count, curve))
            {
                parsed.HitObjects.Add(circle);
                added.Add(circle.Id);
            }

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            afterEdit();
            selection.SetRange(added);
        }

        /// <summary>
        /// Builds the stream circles for a slider. The circles are placed on the beat-snap <em>tick grid</em> in
        /// time (one per tick, starting at the slider's start), so adding more circles simply extends the stream
        /// onto the following ticks - even past where the original slider ended. <paramref name="curve"/> only
        /// ramps the <em>playfield position</em> along the path (a purely visual acceleration that never moves a
        /// circle off its tick) - see <see cref="streamSpacing"/>. The first circle inherits the slider's
        /// new-combo flag. Pure (no mutation).
        /// </summary>
        private List<HitObjectModel> buildStreamCircles(HitObjectModel slider, int count, float curve)
        {
            if (slider.Path is not { Count: >= 2 })
                return new List<HitObjectModel>();

            count = Math.Clamp(count, 1, 256);
            bool firstNewCombo = HitObjectLineEditor.HasNewCombo(slider.RawLine);
            int slides = Math.Max(1, slider.Slides);

            // Time step between circles = one beat-snap tick (constant). This is what fixes the circles to the
            // timeline grid and lets the stream run past the original slider's duration when count is raised.
            double step = beatLengthAt(slider.StartTime) / beatDivisor.Value.Value;
            if (step <= 0)
                step = count > 1 ? slider.Duration / (count - 1) : slider.Duration;

            // Position-only spacing curve: where along the path each circle sits (visual acceleration).
            double[] u = streamSpacing(count, curve);

            var result = new List<HitObjectModel>(count);
            int newId = nextId();

            for (int i = 0; i < count; i++)
            {
                // Map the (warped) progress onto the path, bouncing back and forth across repeats.
                double travel = u[i] * slides;
                int span = Math.Min((int)travel, slides - 1);
                double within = travel - span;
                double frac = span % 2 == 0 ? within : 1 - within;
                Vector2 pos = samplePath(slider.Path, Math.Clamp(frac, 0, 1));

                int px = Math.Clamp((int)Math.Round(pos.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH);
                int py = Math.Clamp((int)Math.Round(pos.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT);
                // Time is the even tick grid - the curve must NOT affect this, or the circles fall off their ticks.
                int t = (int)Math.Round(slider.StartTime + i * step);
                int type = i == 0 && firstNewCombo ? 0b101 : 0b001;
                string raw = $"{px},{py},{t},{type},0,0:0:0:0:";

                result.Add(new HitObjectModel(px, py, t, HitObjectKind.Circle, null, RawLine: raw, Id: newId));
                newId++;
            }

            return result;
        }

        /// <summary>
        /// Returns the normalised positions (0..1) of <paramref name="count"/> stream circles for a spacing
        /// <paramref name="curve"/> in [-1,1]. The gap between consecutive circles changes <em>linearly</em>
        /// from the first gap to the last (constant acceleration), so the ramp is smooth and progressive rather
        /// than bunched at one end: curve&gt;0 packs the circles toward the start, curve&lt;0 toward the end,
        /// 0 = even spacing.
        /// </summary>
        private static double[] streamSpacing(int count, float curve)
        {
            var pos = new double[Math.Max(count, 1)];
            if (count <= 1)
                return pos;

            int gaps = count - 1;
            // ratio = last gap / first gap. curve in [-1,1] -> ratio in [1/5, 5], applied as a linear ramp.
            double ratio = Math.Pow(5, curve);

            double total = 0;
            for (int i = 0; i < gaps; i++)
            {
                double f = gaps > 1 ? (double)i / (gaps - 1) : 0;
                total += 1 + (ratio - 1) * f;
            }

            double acc = 0;
            for (int j = 1; j < count; j++)
            {
                double f = gaps > 1 ? (double)(j - 1) / (gaps - 1) : 0;
                acc += 1 + (ratio - 1) * f;
                pos[j] = acc / total;
            }

            return pos;
        }

        /// <summary>Samples a polyline at a fraction (0..1) of its total length.</summary>
        private static Vector2 samplePath(IReadOnlyList<Vector2> path, double fraction)
            => SliderGeometry.PointAtFraction(path, fraction);

        // --- Timing points ---

        public int AddTimingPoint(TimingPointModel point)
        {
            pushUndo();
            int id = nextTimingPointId();
            parsed.TimingPointModels.Add(point with { Id = id });
            afterTimingEdit(); // RebuildTimingDerived re-sorts (red-before-green on a tie), so no manual sort here
            return id;
        }

        /// <summary>
        /// Inserts a timing point at <paramref name="time"/>: an uninherited red (BPM) line carrying the active
        /// tempo, or an inherited green (SV) line carrying the velocity currently in force there - both inheriting
        /// the surrounding sample context so only the intended property changes. Bound to Ctrl/Cmd+P (BPM) and
        /// Ctrl/Cmd+Shift+P (SV), and to the timeline's Shift-hover add-pill. A same-colour point already on that
        /// tick is left untouched (no duplicate).
        /// </summary>
        public void AddTimingPointAt(double time, bool uninherited)
        {
            int t = (int)Math.Round(time);

            if (parsed.TimingPointModels.Any(tp => tp.Uninherited == uninherited && (int)Math.Round(tp.Time) == t))
                return;

            TimingPointModel? ctx = null;
            double bestTime = double.NegativeInfinity;
            foreach (var tp in parsed.TimingPointModels)
                if (tp.Time <= t && tp.Time >= bestTime) { bestTime = tp.Time; ctx = tp; }

            double beatLength = uninherited
                ? beatLengthAt(time)
                : TimingPointLineEditor.BeatLengthFromSv(Math.Clamp(velocityAt(time), 0.1, 10));

            AddTimingPoint(new TimingPointModel(
                Id: 0,
                Time: t,
                BeatLength: beatLength,
                Meter: ctx?.Meter ?? 4,
                SampleSet: ctx?.SampleSet ?? 0,
                SampleIndex: ctx?.SampleIndex ?? 0,
                Volume: ctx?.Volume ?? 100,
                Uninherited: uninherited,
                Effects: TimingPointLineEditor.WithKiai(0, ctx?.Kiai ?? false)));
        }

        public void UpdateTimingPoint(TimingPointModel point)
        {
            int index = parsed.TimingPointModels.FindIndex(tp => tp.Id == point.Id);
            if (index < 0)
                return;

            pushUndo();
            parsed.TimingPointModels[index] = point;
            afterTimingEdit();
        }

        public void DeleteTimingPoint(int id)
        {
            int index = parsed.TimingPointModels.FindIndex(tp => tp.Id == id);
            if (index < 0)
                return;

            pushUndo();
            parsed.TimingPointModels.RemoveAt(index);
            afterTimingEdit();
        }

        public void UpdateTimingPoints(IReadOnlyList<TimingPointModel> points)
        {
            if (points.Count == 0)
                return;

            pushUndo();
            bool any = false;
            foreach (var point in points)
            {
                int index = parsed.TimingPointModels.FindIndex(tp => tp.Id == point.Id);
                if (index < 0)
                    continue;
                parsed.TimingPointModels[index] = point;
                any = true;
            }

            if (any)
                afterTimingEdit();
            else
                undoStack.Pop(); // nothing matched; drop the snapshot we just pushed
        }

        public void DeleteTimingPoints(IReadOnlyCollection<int> ids)
        {
            if (ids.Count == 0)
                return;

            pushUndo();
            int removed = parsed.TimingPointModels.RemoveAll(tp => ids.Contains(tp.Id));

            if (removed > 0)
                afterTimingEdit();
            else
                undoStack.Pop();
        }

        /// <summary>Seeks the playhead to a time (e.g. double-clicking a timing point jumps to it on the map).</summary>
        public void SeekTo(double time) => seekTo(time);

        private int nextTimingPointId() =>
            parsed.TimingPointModels.Count == 0 ? 0 : parsed.TimingPointModels.Max(tp => tp.Id) + 1;

        /// <summary>Re-derives timing state after a timing-point edit and refreshes the timeline + HUD.</summary>
        private void afterTimingEdit()
        {
            parsed.RebuildTimingDerived();
            // A BPM/SV change re-times existing sliders (their pixel length is unchanged).
            recomputeSliderDurationsData();
            rebuildHitObjects();
            rebuildSampleEvents();
            topTimeline.Rebuild();
            bottomTimeline.Rebuild();
            editable.IsDirty.Value = true;
        }

        public void EndMove()
        {
            if (moveSnapshot.Count == 0)
                return;

            // A drag that didn't actually move anything leaves the map untouched.
            if (moveTimeDelta == 0 && movePosDelta == Vector2.Zero)
            {
                moveSnapshot.Clear();
                return;
            }

            pushUndo();

            if (moveTimeDelta != 0)
                commitTimeMove(moveTimeDelta);
            else if (movePosDelta != Vector2.Zero)
                commitPositionMove((int)movePosDelta.X, (int)movePosDelta.Y);

            moveSnapshot.Clear();

            // Restore time order, then rebuild (which clears the preview transforms).
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            afterEdit();
        }

        public void BeginSliderRepeatDrag(int id)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return;

            repeatDragId = id;
            repeatDragOriginal = parsed.HitObjects[idx];
            repeatDragSnapshot = takeSnapshot();
            repeatDragChanged = false;
        }

        public void DragSliderRepeatTo(double endTime)
        {
            if (repeatDragId < 0)
                return;

            int idx = parsed.HitObjects.FindIndex(o => o.Id == repeatDragId);
            if (idx < 0)
                return;

            var orig = repeatDragOriginal;

            // The duration of a single span (one head-to-tail traversal), fixed by the slider's path + velocity.
            double spanDuration = orig.Duration / Math.Max(1, orig.Slides);
            if (spanDuration <= 0)
                return;

            // osu!lazer: RepeatCount = round(proposedDuration / spanDuration) - 1, so slides = max(1, round(...)).
            double proposedDuration = snapTime(endTime) - orig.StartTime;
            int newSlides = Math.Max(1, (int)Math.Round(proposedDuration / spanDuration));

            var cur = parsed.HitObjects[idx];
            if (cur.Slides == newSlides)
                return;

            parsed.HitObjects[idx] = cur with
            {
                Slides = newSlides,
                Duration = spanDuration * newSlides,
                RawLine = HitObjectLineEditor.SetSliderSlides(cur.RawLine, newSlides),
            };

            repeatDragChanged = true;

            // Live refresh so the timeline bar + playfield reverse arrows track the drag.
            rebuildHitObjects();
            topTimeline.Rebuild();
        }

        public void EndSliderRepeatDrag()
        {
            if (repeatDragId < 0)
                return;

            if (repeatDragChanged && repeatDragSnapshot != null)
            {
                // Push the pre-drag snapshot as a single undo step (the live edits weren't recorded).
                undoStack.Push(repeatDragSnapshot);
                redoStack.Clear();
                afterEdit();
                selection.SetSingle(repeatDragId);
            }

            repeatDragId = -1;
            repeatDragSnapshot = null;
        }

        // --- Slider velocity drag on the timeline (Shift + dragging the tail): change speed, not reverses ---

        public void BeginSliderVelocityDrag(int id)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
            {
                velocityDragId = -1;
                return;
            }

            var o = parsed.HitObjects[idx];
            velocityDragId = id;
            velocityDragOriginal = o;
            velocityDragSnapshot = takeSnapshot();
            velocityDragChanged = false;
            velocityDragPixelLength = originalPixelLength(o);
            velocityDragResetSv = velocityBefore(o.StartTime);
            velocityDragOldEnd = (int)Math.Round(o.StartTime + o.Duration);
            velocityDragPendingSv = velocityAt(o.StartTime);
            velocityDragPendingDuration = o.Duration;
        }

        public void DragSliderVelocityTo(double endTime)
        {
            if (velocityDragId < 0)
                return;

            int idx = parsed.HitObjects.FindIndex(o => o.Id == velocityDragId);
            if (idx < 0)
                return;

            var orig = velocityDragOriginal;
            int slides = Math.Max(1, orig.Slides);
            double beatLength = beatLengthAt(orig.StartTime);
            double mult = parsed.SliderMultiplier;
            if (velocityDragPixelLength <= 0 || beatLength <= 0 || mult <= 0)
                return;

            // Drag the tail to a snapped time: with the path length fixed, the duration sets the speed (SV is
            // inversely proportional to duration). Derive SV, clamp it, then re-derive the duration it really yields.
            double rawDuration = snapTime(endTime) - orig.StartTime;
            double sv = rawDuration > 0
                ? Math.Clamp(velocityDragPixelLength * beatLength * slides / (mult * 100 * rawDuration), 0.1, 10)
                : 10;
            double newDuration = velocityDragPixelLength * beatLength * slides / (mult * 100 * sv);

            velocityDragPendingSv = sv;
            velocityDragPendingDuration = newDuration;

            var cur = parsed.HitObjects[idx];
            if (Math.Abs(cur.Duration - newDuration) < 1e-3)
                return;

            // Live preview: nudge the duration directly (the real green lines are written on drag end).
            parsed.HitObjects[idx] = cur with { Duration = newDuration };
            velocityDragChanged = true;
            rebuildHitObjects();
            topTimeline.Rebuild();
        }

        public void EndSliderVelocityDrag()
        {
            if (velocityDragId < 0)
                return;

            if (velocityDragChanged && velocityDragSnapshot != null)
            {
                int idx = parsed.HitObjects.FindIndex(o => o.Id == velocityDragId);
                if (idx >= 0)
                {
                    applySliderVelocityLine(parsed.HitObjects[idx].StartTime, velocityDragPendingSv);
                    parsed.RebuildTimingDerived();
                    recomputeSliderDurationsData();
                    applyVelocityResetLine(velocityDragId, velocityDragOldEnd, velocityDragResetSv, velocityDragPendingSv);

                    undoStack.Push(velocityDragSnapshot);
                    redoStack.Clear();
                    afterEdit();
                    selection.SetSingle(velocityDragId);
                }
            }

            velocityDragId = -1;
            velocityDragSnapshot = null;
        }

        // --- Slider length / velocity drag (dragging the tail end-cap on the playfield, like osu!lazer) ---

        public void BeginSliderLengthDrag(int id, Vector2 osuCursor)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider
                || parsed.HitObjects[idx].ControlPoints is not { Count: >= 2 })
            {
                lengthDragId = -1;
                return;
            }

            var o = parsed.HitObjects[idx];
            lengthDragId = id;
            lengthDragOriginal = o;
            lengthDragSnapshot = takeSnapshot();
            lengthDragChanged = false;
            lengthDragVelocityMode = false;

            // The full control-polygon curve (no expected distance applied) bounds how far the tail can extend.
            lengthDragFullPath = new List<Vector2>(SliderGeometry.ComputePath(o.ControlPoints!));
            lengthDragCalculatedDistance = SliderGeometry.PathLength(lengthDragFullPath);
            lengthDragOldExpected = SliderGeometry.PathLength(o.Path ?? System.Array.Empty<Vector2>());
            lengthDragOldVelocity = velocityAt(o.StartTime);
            lengthDragOldSpanDuration = sliderDuration(o.StartTime, lengthDragOldExpected, 1);
            lengthDragResetSv = velocityBefore(o.StartTime);
            lengthDragOldEnd = (int)Math.Round(o.StartTime + o.Duration);

            Vector2 tail = positionAtDistance(lengthDragFullPath, lengthDragOldExpected);
            lengthDragGrabOffset = osuCursor - tail;
            lengthDragPendingExpected = lengthDragOldExpected;
            lengthDragPendingDuration = o.Duration;
            lengthDragPendingSv = lengthDragOldVelocity;
        }

        public IReadOnlyList<Vector2> PreviewSliderLength(Vector2 osuCursor, bool adjustVelocity)
        {
            if (lengthDragId < 0)
                return System.Array.Empty<Vector2>();

            var orig = lengthDragOriginal;
            Vector2 desired = osuCursor - lengthDragGrabOffset;
            double proposed = findClosestPathDistance(lengthDragFullPath, lengthDragCalculatedDistance, desired);

            double newExpected;
            double newDuration;
            double newSv = lengthDragOldVelocity;

            if (adjustVelocity && lengthDragOldExpected > 0 && lengthDragOldVelocity > 0)
            {
                // Shift: keep the span duration fixed and let the velocity absorb the length change (lazer's mode).
                // For a fixed duration, velocity is proportional to distance, so newSv = oldSv * proposed/oldExpected.
                double ratio = proposed / lengthDragOldExpected;
                newSv = Math.Clamp(lengthDragOldVelocity * ratio, 0.1, 10);
                // Re-derive the achievable distance from the clamped velocity so the body matches what persists.
                newExpected = Math.Clamp(lengthDragOldExpected * (newSv / lengthDragOldVelocity), 1, lengthDragCalculatedDistance);
                newDuration = lengthDragOldSpanDuration * Math.Max(1, orig.Slides);
                lengthDragVelocityMode = true;
            }
            else
            {
                // Default: change the expected distance, tick-snapped (round down) and capped at the drawn curve.
                double minTick = Math.Min(placementMinLength(orig.StartTime), lengthDragCalculatedDistance);
                proposed = Math.Clamp(proposed, minTick, lengthDragCalculatedDistance);
                newExpected = Math.Clamp(snapSliderLength(orig.StartTime, proposed), minTick, lengthDragCalculatedDistance);
                newDuration = sliderDuration(orig.StartTime, newExpected, orig.Slides);
                lengthDragVelocityMode = false;
            }

            var path = SliderGeometry.ComputePath(orig.ControlPoints!, newExpected);
            if (path.Count < 2)
                return System.Array.Empty<Vector2>();

            lengthDragPendingExpected = newExpected;
            lengthDragPendingDuration = newDuration;
            lengthDragPendingSv = newSv;
            lengthDragChanged = Math.Abs(newExpected - lengthDragOldExpected) > 1e-3
                || (lengthDragVelocityMode && Math.Abs(newSv - lengthDragOldVelocity) > 1e-4);

            // Live timeline readout of the new extent.
            topTimeline.ShowSliderPreview(orig.StartTime, newDuration, beatLengthAt(orig.StartTime));
            return path;
        }

        public void EndSliderLengthDrag()
        {
            if (lengthDragId < 0)
                return;

            topTimeline.ClearSliderPreview();

            if (lengthDragChanged && lengthDragSnapshot != null)
            {
                int idx = parsed.HitObjects.FindIndex(o => o.Id == lengthDragId);
                if (idx >= 0)
                {
                    var o = parsed.HitObjects[idx];
                    var newPath = SliderGeometry.ComputePath(o.ControlPoints!, lengthDragPendingExpected);
                    if (newPath.Count >= 2)
                    {
                        parsed.HitObjects[idx] = o with
                        {
                            Path = newPath,
                            Duration = lengthDragPendingDuration,
                            RawLine = HitObjectLineEditor.SetSliderCurve(o.RawLine, o.ControlPoints!, lengthDragPendingExpected),
                        };

                        // Velocity mode persists as a green (inherited) SV line at the slider's start, the only
                        // place the stable .osu format can carry a per-slider speed; a matching reset line at the
                        // tail restores the speed the section ran at (e.g. 1.5 -> 2.94 -> 1.5).
                        if (lengthDragVelocityMode)
                        {
                            applySliderVelocityLine(o.StartTime, lengthDragPendingSv);
                            parsed.RebuildTimingDerived();
                            recomputeSliderDurationsData();
                            applyVelocityResetLine(lengthDragId, lengthDragOldEnd, lengthDragResetSv, lengthDragPendingSv);
                        }

                        undoStack.Push(lengthDragSnapshot);
                        redoStack.Clear();
                        afterEdit();
                        selection.SetSingle(lengthDragId);
                    }
                }
            }

            lengthDragId = -1;
            lengthDragSnapshot = null;
            lengthDragVelocityMode = false;
        }

        /// <summary>Position along a piecewise path at the given travel distance (clamped to its ends).</summary>
        private static Vector2 positionAtDistance(IReadOnlyList<Vector2> path, double distance)
        {
            if (path.Count == 0)
                return Vector2.Zero;
            if (path.Count == 1 || distance <= 0)
                return path[0];

            double acc = 0;
            for (int i = 1; i < path.Count; i++)
            {
                double seg = (path[i] - path[i - 1]).Length;
                if (acc + seg >= distance)
                {
                    double t = seg > 0 ? (distance - acc) / seg : 0;
                    return path[i - 1] + (path[i] - path[i - 1]) * (float)t;
                }
                acc += seg;
            }
            return path[^1];
        }

        /// <summary>
        /// The travel distance along the curve whose point is closest to <paramref name="desired"/> (osu!lazer's
        /// coarse-then-fine search, with a small bias toward longer distances so the full length is easy to reach).
        /// </summary>
        private static double findClosestPathDistance(IReadOnlyList<Vector2> path, double calculatedDistance, Vector2 desired)
        {
            if (path.Count < 2 || calculatedDistance <= 0)
                return 0;

            const double step1 = 10, step2 = 0.1, bias = 0.01;
            double best = 0, min = double.MaxValue;

            for (double d = 0; d <= calculatedDistance; d += step1)
            {
                double dist = Vector2.Distance(positionAtDistance(path, d), desired) - d * bias;
                if (dist < min) { min = dist; best = d; }
            }

            double maxV = Math.Min(best + step1, calculatedDistance);
            for (double d = Math.Max(0, best - step1); d <= maxV; d += step2)
            {
                double dist = Vector2.Distance(positionAtDistance(path, d), desired) - d * bias;
                if (dist < min) { min = dist; best = d; }
            }

            return best;
        }

        /// <summary>
        /// Sets the slider-velocity in force at <paramref name="time"/> to <paramref name="sv"/> by updating the
        /// inherited (green) line exactly there, or creating one that inherits the surrounding sample context.
        /// Data only - the caller re-derives timing. Used to persist a per-slider velocity change (stable model).
        /// </summary>
        private void applySliderVelocityLine(double time, double sv)
        {
            sv = Math.Clamp(sv, 0.1, 10);
            double beatLength = TimingPointLineEditor.BeatLengthFromSv(sv);
            int t = (int)Math.Round(time);

            int idx = parsed.TimingPointModels.FindIndex(tp => !tp.Uninherited && (int)Math.Round(tp.Time) == t);
            if (idx >= 0)
            {
                parsed.TimingPointModels[idx] = parsed.TimingPointModels[idx] with { BeatLength = beatLength };
                return;
            }

            // Inherit the active context so the new line changes only the SV, not the hitsound volume/bank/kiai.
            TimingPointModel? ctx = null;
            double bestTime = double.NegativeInfinity;
            foreach (var tp in parsed.TimingPointModels)
                if (tp.Time <= t && tp.Time >= bestTime) { bestTime = tp.Time; ctx = tp; }

            parsed.TimingPointModels.Add(new TimingPointModel(
                Id: nextTimingPointId(),
                Time: t,
                BeatLength: beatLength,
                Meter: ctx?.Meter ?? 4,
                SampleSet: ctx?.SampleSet ?? 0,
                SampleIndex: ctx?.SampleIndex ?? 0,
                Volume: ctx?.Volume ?? 100,
                Uninherited: false,
                Effects: TimingPointLineEditor.WithKiai(0, ctx?.Kiai ?? false)));

            // Keep the list ordered (red before green on a tie) so the encoded .osu stays valid.
            parsed.TimingPointModels.Sort((a, b) =>
                a.Time != b.Time ? a.Time.CompareTo(b.Time) : b.Uninherited.CompareTo(a.Uninherited));
        }

        /// <summary>
        /// Removes the auto-added "reset" green line at <paramref name="time"/> whose SV matches <paramref name="sv"/>
        /// (the one a velocity edit drops past a slider). Used to relocate it when the slider is re-modified, so the
        /// reset point follows the slider's tail instead of leaving a stale line at the old end.
        /// </summary>
        private void removeVelocityResetLine(int time, double sv)
        {
            double beat = TimingPointLineEditor.BeatLengthFromSv(Math.Clamp(sv, 0.1, 10));
            parsed.TimingPointModels.RemoveAll(tp => !tp.Uninherited
                && (int)Math.Round(tp.Time) == time
                && Math.Abs(tp.BeatLength - beat) < 1e-3);
        }

        /// <summary>
        /// Places (or relocates) the reset green line at a velocity-edited slider's tail. Removes any stale reset
        /// line left at the previous tail (so it follows the slider when re-modified), then writes one at the new
        /// tail restoring <paramref name="resetSv"/> - unless the edit returned the slider to that base speed, in
        /// which case no reset line is needed. Assumes slider durations are already recomputed.
        /// </summary>
        private void applyVelocityResetLine(int sliderId, int oldEnd, double resetSv, double appliedSv)
        {
            var slider = parsed.HitObjects.First(h => h.Id == sliderId);
            int newEnd = (int)Math.Round(slider.StartTime + slider.Duration);

            if (oldEnd != newEnd)
                removeVelocityResetLine(oldEnd, resetSv);

            if (Math.Abs(appliedSv - resetSv) > 1e-4)
            {
                applySliderVelocityLine(newEnd, resetSv);
                parsed.RebuildTimingDerived();
                recomputeSliderDurationsData();
            }
            else
                removeVelocityResetLine(newEnd, resetSv);
        }

        public void BeginSpinnerDurationDrag(int id)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Spinner)
                return;

            spinnerDragId = id;
            spinnerDragOriginal = parsed.HitObjects[idx];
            spinnerDragSnapshot = takeSnapshot();
            spinnerDragChanged = false;
        }

        public void DragSpinnerEndTo(double endTime)
        {
            if (spinnerDragId < 0)
                return;

            int idx = parsed.HitObjects.FindIndex(o => o.Id == spinnerDragId);
            if (idx < 0)
                return;

            var orig = spinnerDragOriginal;

            // The end snaps to the beat grid but stays at least one beat after the start (like lazer's minimum).
            double minEnd = orig.StartTime + beatLengthAt(orig.StartTime);
            int newEnd = (int)Math.Round(Math.Max(minEnd, snapTime(endTime)));

            var cur = parsed.HitObjects[idx];
            if ((int)(cur.StartTime + cur.Duration) == newEnd)
                return;

            parsed.HitObjects[idx] = cur with
            {
                Duration = Math.Max(0, newEnd - cur.StartTime),
                RawLine = HitObjectLineEditor.SetSpinnerEndTime(cur.RawLine, newEnd),
            };

            spinnerDragChanged = true;

            // Live refresh so the timeline bar tracks the drag.
            rebuildHitObjects();
            topTimeline.Rebuild();
        }

        public void EndSpinnerDurationDrag()
        {
            if (spinnerDragId < 0)
                return;

            if (spinnerDragChanged && spinnerDragSnapshot != null)
            {
                undoStack.Push(spinnerDragSnapshot);
                redoStack.Clear();
                afterEdit();
                selection.SetSingle(spinnerDragId);
            }

            spinnerDragId = -1;
            spinnerDragSnapshot = null;
        }

        // --- Selection transforms (rotate / scale / flip), mirroring osu!lazer's GeometryUtils math ---

        /// <summary>Movable (non-spinner) selected objects, in time order.</summary>
        private IEnumerable<HitObjectModel> movableSelection() =>
            parsed.HitObjects.Where(o => selection.Contains(o.Id) && o.Kind != HitObjectKind.Spinner);

        /// <summary>The osu!pixel points that bound an object (a circle's centre; a slider's whole path).</summary>
        private static IEnumerable<Vector2> extentPoints(HitObjectModel o)
        {
            if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 0 } path)
            {
                foreach (var p in path)
                    yield return p;
            }
            else
                yield return new Vector2(o.X, o.Y);
        }

        public RectangleF? SelectionBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;

            foreach (var o in movableSelection())
            foreach (var p in extentPoints(o))
            {
                any = true;
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            if (!any)
                return null;

            // Follow a live (uncommitted) position drag so the box tracks the objects as they move.
            if (moveSnapshot.Count > 0 && movePosDelta != Vector2.Zero)
                return new RectangleF(minX + movePosDelta.X, minY + movePosDelta.Y, maxX - minX, maxY - minY);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public void BeginSelectionTransform()
        {
            if (!prepareTransform())
                return;

            transformUndo = takeSnapshot();
            transformChanged = false;
        }

        public void RotateSelection(float degrees)
        {
            if (transformSnapshot == null)
                return;

            Vector2 centre = transformQuad.Centre;
            Func<Vector2, Vector2> map = p => rotateAround(p, centre, degrees);

            // Refuse a rotation that would push the selection off the playfield: clamping it back in would distort
            // (resize) the slider, and repeating that compounds into runaway growth. Flash red instead.
            if (!transformStaysInBounds(map))
            {
                playfield.FlashSelectionBlocked();
                return;
            }

            applySelectionMap(map);
        }

        /// <summary>
        /// True if mapping every snapshot object's geometry through <paramref name="map"/> keeps it inside the
        /// playfield, so no clamping (and thus no slider resize/distortion) is needed. Slider control points and
        /// circle positions are the points <see cref="clampControlPoints"/> would otherwise pull back in.
        /// </summary>
        private bool transformStaysInBounds(Func<Vector2, Vector2> map)
        {
            if (transformSnapshot == null)
                return false;

            foreach (var o in transformSnapshot.Values)
            {
                if (o.Kind == HitObjectKind.Slider && o.ControlPoints is { Count: >= 1 } cps)
                {
                    foreach (var cp in cps)
                        if (!inPlayfield(map(cp.Position)))
                            return false;
                }
                else if (o.Kind == HitObjectKind.Circle)
                {
                    if (!inPlayfield(map(new Vector2(o.X, o.Y))))
                        return false;
                }
            }

            return true;
        }

        private static bool inPlayfield(Vector2 p) =>
            p.X >= 0 && p.X <= ParsedBeatmap.PLAYFIELD_WIDTH && p.Y >= 0 && p.Y <= ParsedBeatmap.PLAYFIELD_HEIGHT;

        public void ScaleSelection(Vector2 scaleDelta, Anchor reference)
        {
            if (transformSnapshot == null)
                return;

            applySelectionMap(p => scaledPosition(reference, scaleDelta, transformQuad, p));
        }

        public void EndSelectionTransform()
        {
            if (transformSnapshot == null)
                return;

            if (transformChanged && transformUndo != null)
            {
                undoStack.Push(transformUndo);
                redoStack.Clear();
                afterEdit();
            }

            transformSnapshot = null;
            transformUndo = null;
        }

        /// <summary>
        /// Rotates the selection by a fixed angle as a single committed edit (used by the Ctrl+Shift+R dialog).
        /// The pivot is the playfield centre (256,192) or the selection's bounding-box centre.
        /// </summary>
        public void RotateSelectionBy(float degrees, bool aroundPlayfieldCentre)
        {
            if (!prepareTransform())
                return;

            Vector2 centre = aroundPlayfieldCentre
                ? new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f)
                : transformQuad.Centre;

            Func<Vector2, Vector2> map = p => rotateAround(p, centre, degrees);

            // Block (and flash) a rotation that wouldn't fit rather than clamp-resizing the slider - see RotateSelection.
            if (!transformStaysInBounds(map))
            {
                playfield.FlashSelectionBlocked();
                transformSnapshot = null;
                return;
            }

            transformUndo = takeSnapshot();
            transformChanged = false;

            applySelectionMap(map);

            if (transformChanged && transformUndo != null)
            {
                undoStack.Push(transformUndo);
                redoStack.Clear();
                afterEdit();
            }

            transformSnapshot = null;
            transformUndo = null;
        }

        // --- Quick eased rotation (the Ctrl/Cmd +,/. shortcuts spin the selection into place instead of snapping) ---
        private bool rotationAnimating;
        private double rotationElapsed;
        private float rotationTarget;
        private Vector2 rotationCentre;
        private const double rotation_anim_duration = 120;

        /// <summary>
        /// Rotates the selection by a fixed angle with a quick eased spin (the Ctrl/Cmd +,/. shortcuts). Drives the
        /// same live transform pipeline as the Shift-box rotate, re-rendering each frame from the pre-spin snapshot,
        /// then commits a single undo step when the spin lands. Blocked (and flashed) if the final angle won't fit.
        /// </summary>
        public void RotateSelectionAnimated(float degrees, bool aroundPlayfieldCentre)
        {
            // A second press mid-spin commits the current one first, so rapid taps stack cleanly.
            if (rotationAnimating)
                finishRotationAnimation();

            if (!prepareTransform())
                return;

            Vector2 centre = aroundPlayfieldCentre
                ? new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f)
                : transformQuad.Centre;

            // Block (and flash) a rotation that wouldn't fit - checked against the final angle, like the instant path.
            if (!transformStaysInBounds(p => rotateAround(p, centre, degrees)))
            {
                playfield.FlashSelectionBlocked();
                transformSnapshot = null;
                return;
            }

            transformUndo = takeSnapshot();
            transformChanged = false;

            rotationCentre = centre;
            rotationTarget = degrees;
            rotationElapsed = 0;
            rotationAnimating = true;
        }

        /// <summary>Advances the in-flight rotation spin one frame (called from <see cref="Update"/>).</summary>
        private void tickRotationAnimation()
        {
            if (!rotationAnimating)
                return;

            rotationElapsed += Time.Elapsed;
            double t = Math.Clamp(rotationElapsed / rotation_anim_duration, 0, 1);
            float angle = rotationTarget * (float)Interpolation.ApplyEasing(Easing.OutQuint, t);
            applySelectionMap(p => rotateAround(p, rotationCentre, angle));

            if (t >= 1)
                finishRotationAnimation();
        }

        /// <summary>Lands the spin exactly on the target angle and commits it as a single undo step.</summary>
        private void finishRotationAnimation()
        {
            if (!rotationAnimating)
                return;

            rotationAnimating = false;
            applySelectionMap(p => rotateAround(p, rotationCentre, rotationTarget));

            if (transformChanged && transformUndo != null)
            {
                undoStack.Push(transformUndo);
                redoStack.Clear();
                afterEdit();
            }

            transformSnapshot = null;
            transformUndo = null;
        }

        public void FlipSelection(bool horizontal)
        {
            if (!prepareTransform())
                return;

            Vector2 centre = transformQuad.Centre;
            transformUndo = takeSnapshot();
            transformChanged = false;

            applySelectionMap(p => horizontal
                ? new Vector2(2 * centre.X - p.X, p.Y)
                : new Vector2(p.X, 2 * centre.Y - p.Y));

            if (transformChanged)
            {
                undoStack.Push(transformUndo!);
                redoStack.Clear();
                afterEdit();
            }

            transformSnapshot = null;
            transformUndo = null;
        }

        /// <summary>
        /// Reverses the selected pattern (Ctrl+G), like osu!lazer's HandleReverse: mirrors the objects' start
        /// times within the selection's span, reverses each slider's path, and keeps the new-combo flags at
        /// the same ordinal positions.
        /// </summary>
        public void ReverseSelection()
        {
            var sel = parsed.HitObjects.Where(o => selection.Contains(o.Id)).OrderBy(o => o.StartTime).ToList();
            if (sel.Count == 0)
                return;

            bool many = sel.Count > 1;
            double startTime = sel.Min(o => o.StartTime);
            double endTime = sel.Max(o => o.StartTime + o.Duration);
            var newComboOrder = sel.Select(o => (rawType(o.RawLine) & 0b100) != 0).ToList();

            pushUndo();

            var updated = new Dictionary<int, HitObjectModel>();
            foreach (var o in sel)
            {
                var n = o;

                if (many)
                {
                    double objEnd = o.StartTime + o.Duration;
                    double newStart = endTime - (objEnd - startTime);
                    n = n with { StartTime = newStart, RawLine = HitObjectLineEditor.ShiftTime(n.RawLine, newStart - o.StartTime) };
                }

                if (o.Kind == HitObjectKind.Slider && o.ControlPoints is { Count: >= 2 } cps)
                    n = reverseSlider(n, cps);

                updated[o.Id] = n;
            }

            for (int i = 0; i < parsed.HitObjects.Count; i++)
                if (updated.TryGetValue(parsed.HitObjects[i].Id, out var n))
                    parsed.HitObjects[i] = n;

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // Restore the new-combo flags to the same ordinal positions within the re-sorted selection.
            var resorted = parsed.HitObjects.Where(o => selection.Contains(o.Id)).OrderBy(o => o.StartTime).ToList();
            for (int i = 0; i < resorted.Count && i < newComboOrder.Count; i++)
            {
                bool cur = (rawType(resorted[i].RawLine) & 0b100) != 0;
                if (cur != newComboOrder[i])
                {
                    int idx = parsed.HitObjects.FindIndex(o => o.Id == resorted[i].Id);
                    parsed.HitObjects[idx] = parsed.HitObjects[idx] with { RawLine = HitObjectLineEditor.ToggleNewCombo(parsed.HitObjects[idx].RawLine) };
                }
            }

            afterEdit();
        }

        /// <summary>Reverses a slider's control-point order (re-inferring segment types) so it runs the other way.</summary>
        private HitObjectModel reverseSlider(HitObjectModel o, IReadOnlyList<SliderControlPoint> cps)
        {
            // Port of osu!lazer's slider reverse (SliderPathExtensions.reverseControlPoints): reverse the control
            // points while propagating each segment's type forward, keep the expected distance unchanged, and move
            // the head to the old slider end. The previous implementation discarded the control-point types and
            // re-derived the length from the raw hull, which reshaped/resized the slider.
            int n = cps.Count;
            double pixelLength = originalPixelLength(o);

            // The reversed slider starts where the old one ended (its path end at the expected distance).
            Vector2 newStart = o.Path is { Count: > 0 } p ? p[^1] : cps[^1].Position;

            var rev = new SliderControlPoint[n];
            SliderPathType? lastType = null;

            for (int i = 0; i < n; i++)
            {
                var pt = cps[i];
                SliderPathType? type;

                if (i == n - 1)
                    type = lastType;               // old head becomes the new tail; its type carries forward
                else if (pt.Type != null)
                {
                    type = lastType;               // this boundary inherits the previous segment's type...
                    lastType = pt.Type;            // ...and hands its own type to the next boundary upstream
                }
                else
                    type = null;

                rev[n - 1 - i] = new SliderControlPoint(pt.Position, type);
            }

            // The new head must start a segment and sit at the old slider end.
            rev[0] = new SliderControlPoint(newStart, rev[0].Type ?? SliderPathType.Bezier);

            var typed = new List<SliderControlPoint>(rev);
            var path = SliderGeometry.ComputePath(typed, pixelLength);
            if (path.Count < 2 || pixelLength < 1)
                return o;

            return o with
            {
                X = (int)Math.Round(newStart.X),
                Y = (int)Math.Round(newStart.Y),
                Path = path,
                ControlPoints = typed,
                Duration = sliderDuration(o.StartTime, pixelLength, o.Slides),
                RawLine = HitObjectLineEditor.SetSliderCurve(o.RawLine, typed, pixelLength),
            };
        }

        /// <summary>Re-times every slider's travel duration for the current slider velocity (pixel length unchanged).</summary>
        private void recomputeSliderDurations()
        {
            recomputeSliderDurationsData();
            rebuildHitObjects();
            rebuildSampleEvents();
            topTimeline.Rebuild(); // slider widths on the timeline depend on duration
        }

        /// <summary>
        /// Re-times every slider's travel duration for the current SV (global multiplier + active green line),
        /// keeping its pixel length fixed - so a slider's playfield size is unchanged but its timeline span /
        /// speed reflects the new velocity. Data only; the caller refreshes the views.
        /// </summary>
        private void recomputeSliderDurationsData()
        {
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                var o = parsed.HitObjects[i];
                if (o.Kind != HitObjectKind.Slider)
                    continue;

                double pixelLength = originalPixelLength(o);
                parsed.HitObjects[i] = o with { Duration = sliderDuration(o.StartTime, pixelLength, o.Slides) };
            }
        }

        /// <summary>The slider's stored expected distance (raw <c>length</c> field), falling back to its path length.</summary>
        private static double originalPixelLength(HitObjectModel o)
        {
            string[] parts = o.RawLine.Split(',');
            if (parts.Length >= 8
                && double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double len) && len > 0)
                return len;

            return o.Path is { Count: > 1 } p ? SliderGeometry.PathLength(p) : 0;
        }

        /// <summary>Captures the current selection + its surrounding quad for a transform; false if nothing movable.</summary>
        private bool prepareTransform()
        {
            var bounds = SelectionBounds();
            if (bounds == null)
                return false;

            transformQuad = bounds.Value;
            transformSnapshot = movableSelection().ToDictionary(o => o.Id, o => o);
            return transformSnapshot.Count > 0;
        }

        /// <summary>Applies an osu!pixel position map to every snapshot object, live (rebuilds the playfield).</summary>
        private void applySelectionMap(Func<Vector2, Vector2> map)
        {
            if (transformSnapshot == null)
                return;

            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                if (transformSnapshot.TryGetValue(parsed.HitObjects[i].Id, out var orig))
                    parsed.HitObjects[i] = transformObject(orig, map);
            }

            transformChanged = true;
            rebuildHitObjects();
        }

        /// <summary>Maps a single object's geometry (circle position, or a slider's whole control-point set).</summary>
        private HitObjectModel transformObject(HitObjectModel orig, Func<Vector2, Vector2> map)
        {
            if (orig.Kind == HitObjectKind.Slider && orig.ControlPoints is { Count: >= 2 } cps0)
            {
                var mapped = new List<SliderControlPoint>(cps0.Count);
                foreach (var cp in cps0)
                {
                    Vector2 np = map(cp.Position);
                    mapped.Add(cp with { X = np.X, Y = np.Y });
                }

                var cps = clampControlPoints(mapped);
                if (cps[0].Type == null)
                    cps[0] = cps[0] with { Type = SliderPathType.Bezier };

                // Keep the slider's stored expected distance: a selection-box rotate/scale only moves the control
                // points (it shouldn't lengthen or shorten the body). Re-deriving the length from the transformed
                // hull made rotating or resizing the box change every slider's size - so we preserve the original.
                double pixelLength = originalPixelLength(orig);
                var path = SliderGeometry.ComputePath(cps, pixelLength);
                if (path.Count < 2 || pixelLength < 1)
                    return orig; // degenerate transform - leave the slider as-is

                return orig with
                {
                    X = (int)Math.Round(cps[0].X),
                    Y = (int)Math.Round(cps[0].Y),
                    Path = path,
                    ControlPoints = cps,
                    Duration = sliderDuration(orig.StartTime, pixelLength, orig.Slides),
                    RawLine = HitObjectLineEditor.SetSliderCurve(orig.RawLine, cps, pixelLength),
                };
            }

            if (orig.Kind == HitObjectKind.Circle)
            {
                Vector2 np = map(new Vector2(orig.X, orig.Y));
                int nx = Math.Clamp((int)Math.Round(np.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH);
                int ny = Math.Clamp((int)Math.Round(np.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT);
                int dx = nx - (int)Math.Round(orig.X);
                int dy = ny - (int)Math.Round(orig.Y);

                return orig with { X = nx, Y = ny, RawLine = HitObjectLineEditor.ShiftPosition(orig.RawLine, dx, dy) };
            }

            return orig;
        }

        /// <summary>Rotates a point around an origin by degrees (osu!lazer's RotatePointAroundOrigin).</summary>
        private static Vector2 rotateAround(Vector2 point, Vector2 origin, float degrees)
        {
            float a = -MathHelper.DegreesToRadians(degrees);
            Vector2 d = point - origin;
            return new Vector2(
                d.X * MathF.Cos(a) + d.Y * MathF.Sin(a),
                d.X * -MathF.Sin(a) + d.Y * MathF.Cos(a)) + origin;
        }

        /// <summary>osu!lazer's GeometryUtils.GetScaledPosition: a width/height delta anchored at a reference corner.</summary>
        private static Vector2 scaledPosition(Anchor reference, Vector2 scale, RectangleF quad, Vector2 position)
        {
            float xOffset = (reference & Anchor.x0) > 0 ? -scale.X : 0;
            float yOffset = (reference & Anchor.y0) > 0 ? -scale.Y : 0;

            if (scale.X != 0 && quad.Width > 0)
                position.X = quad.Left + xOffset + (position.X - quad.Left) / quad.Width * (quad.Width + scale.X);

            if (scale.Y != 0 && quad.Height > 0)
                position.Y = quad.Top + yOffset + (position.Y - quad.Top) / quad.Height * (quad.Height + scale.Y);

            return position;
        }

        private void commitTimeMove(int delta)
        {
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                if (moveSnapshot.TryGetValue(parsed.HitObjects[i].Id, out var original))
                    parsed.HitObjects[i] = original with
                    {
                        StartTime = original.StartTime + delta,
                        RawLine = HitObjectLineEditor.ShiftTime(original.RawLine, delta),
                    };
            }
        }

        private void commitPositionMove(int dx, int dy)
        {
            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                if (!moveSnapshot.TryGetValue(parsed.HitObjects[i].Id, out var original) || original.Kind == HitObjectKind.Spinner)
                    continue;

                parsed.HitObjects[i] = original with
                {
                    X = original.X + dx,
                    Y = original.Y + dy,
                    Path = offsetPath(original.Path, dx, dy),
                    ControlPoints = offsetControlPoints(original.ControlPoints, dx, dy),
                    RawLine = HitObjectLineEditor.ShiftPosition(original.RawLine, dx, dy),
                };
            }
        }

        private static IReadOnlyList<SliderControlPoint>? offsetControlPoints(IReadOnlyList<SliderControlPoint>? controlPoints, int dx, int dy)
        {
            if (controlPoints == null)
                return null;

            var result = new List<SliderControlPoint>(controlPoints.Count);
            foreach (var p in controlPoints)
                result.Add(p with { X = p.X + dx, Y = p.Y + dy });
            return result;
        }

        private static void accumulateBounds(HitObjectModel o, ref Vector2 min, ref Vector2 max)
        {
            var head = new Vector2(o.X, o.Y);
            min = Vector2.ComponentMin(min, head);
            max = Vector2.ComponentMax(max, head);

            if (o.Path == null)
                return;

            foreach (var p in o.Path)
            {
                min = Vector2.ComponentMin(min, p);
                max = Vector2.ComponentMax(max, p);
            }
        }

        private static IReadOnlyList<Vector2>? offsetPath(IReadOnlyList<Vector2>? path, int dx, int dy)
        {
            if (path == null)
                return null;

            var offset = new Vector2(dx, dy);
            var result = new List<Vector2>(path.Count);
            foreach (var p in path)
                result.Add(p + offset);
            return result;
        }

        /// <summary>The active timing point's start (ms) and 1/4-beat step at the given time.</summary>
        private bool tryActiveBeat(double timeMs, out double pointTime, out double step)
        {
            pointTime = 0;
            step = 0;

            if (parsed.BeatPoints.Count == 0)
                return false;

            var point = parsed.BeatPoints[0];
            foreach (var p in parsed.BeatPoints)
            {
                if (p.Time <= timeMs)
                    point = p;
                else
                    break;
            }

            pointTime = point.Time;
            step = point.BeatLength / beatDivisor.Value.Value;
            return step > 0;
        }

        /// <summary>Snaps a time (ms) to the nearest 1/4 beat tick of the active timing point.</summary>
        private double snapTime(double timeMs)
        {
            if (!tryActiveBeat(timeMs, out double pointTime, out double step))
                return timeMs;

            return pointTime + Math.Round((timeMs - pointTime) / step) * step;
        }

        /// <summary>Common post-edit refresh: renumber combos, restack, rebuild views, resync hitsounds, flag dirty.</summary>
        private void afterEdit()
        {
            recomputeCombos();
            applyStacking();
            rebuildHitObjects();
            rebuildSampleEvents();
            topTimeline.Rebuild();
            editable.IsDirty.Value = true;
            scheduleStarRecompute();
        }

        /// <summary>
        /// Recomputes the star rating off the update thread after a short idle (edits often arrive in bursts -
        /// drags, paints, multi-object moves - so we debounce rather than recompute on every micro-edit).
        /// </summary>
        private void scheduleStarRecompute()
        {
            starRecomputeDebounce?.Cancel();
            starRecomputeDebounce = Scheduler.AddDelayed(recomputeStars, 350);
        }

        /// <summary>Snapshots the difficulty + objects (value-type lists) so the calc can run on a background thread.</summary>
        private ParsedBeatmap snapshotForStars()
        {
            var b = new ParsedBeatmap
            {
                CircleSize = editable.Cs.Value,
                OverallDifficulty = editable.Od.Value,
                ApproachRate = editable.Ar.Value,
                SliderMultiplier = editable.SliderMultiplier.Value,
                SliderTickRate = editable.SliderTickRate.Value,
            };
            b.HitObjects.AddRange(parsed.HitObjects);
            b.BeatPoints.AddRange(parsed.BeatPoints);
            return b;
        }

        private void recomputeStars()
        {
            int generation = ++starComputeGeneration;
            var snapshot = snapshotForStars();

            Task.Run(() =>
            {
                double stars = StarRatingCalculator.Calculate(snapshot);
                Schedule(() =>
                {
                    // Drop the result if another recompute has since superseded this one.
                    if (generation != starComputeGeneration)
                        return;

                    currentStars = stars;
                    starChip.Child = new StarRatingDisplay(stars);
                });
            });
        }

        /// <summary>Recomputes stack heights for the current AR window and stack leniency.</summary>
        private void applyStacking() =>
            StackingProcessor.Apply(parsed.HitObjects, ParsedBeatmap.PreemptFor(editable.Ar.Value), parsed.StackLeniency);

        // --- Copy / cut / paste: clipboard of hit objects, time-shifted on paste (lazer-style) ---

        /// <summary>
        /// Copies the selected objects (in time order) to the process-wide clipboard (so they can be pasted
        /// into another map/difficulty), and writes an osu! modding timestamp to the OS clipboard - exactly
        /// like osu!lazer, so Ctrl+C gives you a "00:12:345 (1,2,3) - " string ready to paste into forums/chat.
        /// </summary>
        private void copySelection()
        {
            var ids = new HashSet<int>(selection.Selected);
            var copied = parsed.HitObjects.Where(o => ids.Contains(o.Id)).OrderBy(o => o.StartTime).ToList();

            // The modding timestamp is written even with no selection (just the current time), matching lazer.
            writeModdingTimestamp(copied);

            if (copied.Count > 0)
                HitObjectClipboard.Set(copied);
        }

        /// <summary>Writes lazer's modding timestamp "mm:ss:fff (combo,combo,...) - " to the OS clipboard.</summary>
        private void writeModdingTimestamp(IReadOnlyList<HitObjectModel> objects)
        {
            double time = objects.Count > 0 ? objects.Min(o => o.StartTime) : CurrentTime;
            string stamp = objects.Count > 0
                ? $"{formatTimestamp(time)} ({string.Join(",", objects.Select(o => o.ComboNumber))}) - "
                : $"{formatTimestamp(time)} - ";

            hostClipboard?.SetText(stamp);
        }

        /// <summary>Formats a time (ms) as the osu! editor/modding timestamp "mm:ss:fff".</summary>
        private static string formatTimestamp(double ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";
        }

        private void cutSelection()
        {
            if (selection.Selected.Count == 0)
                return;

            copySelection();
            DeleteSelected();
        }

        /// <summary>
        /// Pastes the clipboard so its earliest object lands on the current snapped time, then selects it.
        /// Reads the process-wide clipboard, so this works across maps/difficulties; pasted slider durations
        /// are re-derived for this map's timing.
        /// </summary>
        private void paste()
        {
            if (!HitObjectClipboard.HasContent)
                return;

            pasteObjects(HitObjectClipboard.Objects);
        }

        /// <summary>
        /// Inserts the given objects (a clipboard paste or a saved Pattern Gallery pattern) at the current
        /// snapped playhead: shifts their times so the earliest lands on the playhead, gives them fresh ids,
        /// re-times sliders to this map's BPM/SV, renumbers combos, and selects the result. When
        /// <paramref name="sourceVelocities"/> is supplied (a pattern import), each slider's source velocity
        /// (SliderMultiplier·SV) is reproduced with green lines so it keeps the same rhythmic length + shape.
        /// </summary>
        public void pasteObjects(IReadOnlyList<HitObjectModel> clip, IReadOnlyList<double?>? sourceVelocities = null, double? sourceBeatLength = null)
        {
            if (clip == null || clip.Count == 0)
                return;

            double target = Math.Round(snapTime(CurrentTime));
            double baseTime = clip.Min(o => o.StartTime);

            // Rescale inter-note spacing by the tempo ratio so a pattern keeps its rhythm (and stays on the beat
            // grid) when pasted into a map with a different BPM. A note k ticks into the pattern lands k ticks
            // into the target tempo from the (snapped) playhead. Falls back to a plain shift for v1 patterns.
            double targetBeatLength = beatLengthAt(target);
            double ratio = sourceBeatLength is double sbl && sbl > 0 && targetBeatLength > 0
                ? targetBeatLength / sbl
                : 1.0;

            pushUndo();

            int id = nextId();
            var newIds = new List<int>(clip.Count);
            var velocityById = new Dictionary<int, double>();
            for (int i = 0; i < clip.Count; i++)
            {
                var o = clip[i];
                double newTime = Math.Round(target + (o.StartTime - baseTime) * ratio);
                newIds.Add(id);
                parsed.HitObjects.Add(o with
                {
                    StartTime = newTime,
                    RawLine = HitObjectLineEditor.ShiftTime(o.RawLine, newTime - o.StartTime),
                    Id = id,
                });

                if (o.Kind == HitObjectKind.Slider && sourceVelocities != null && i < sourceVelocities.Count
                    && sourceVelocities[i] is double v && v > 0)
                    velocityById[id] = v;

                id++;
            }

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            // Re-time sliders to this map's ambient SV/BPM first, so their durations (and the reset-line position)
            // are sane before we add per-slider green lines, then re-time again with those lines applied.
            recomputeSliderDurationsData();
            applyPatternVelocities(velocityById);
            recomputeSliderDurationsData();
            afterEdit();
            selection.SetRange(newIds);
        }

        /// <summary>
        /// For each just-pasted slider that carried a source velocity, writes/updates a green line at its start so
        /// the target effective SV equals <c>sourceVelocity / SliderMultiplier_target</c> - which (with the slider's
        /// pixel length kept) makes it span the same number of beats as in the source map. Processes sliders in
        /// time order so each added line only affects the slider it's for (and later ones reading the same value).
        /// </summary>
        private void applyPatternVelocities(IReadOnlyDictionary<int, double> velocityById)
        {
            double smTarget = parsed.SliderMultiplier;
            if (velocityById.Count == 0 || smTarget <= 0)
                return;

            var ids = parsed.HitObjects
                .Where(o => velocityById.ContainsKey(o.Id))
                .OrderBy(o => o.StartTime)
                .Select(o => o.Id)
                .ToList();
            if (ids.Count == 0)
                return;

            // The SV the map originally runs at just past the pattern; restored afterwards so the green lines we
            // add affect only the pattern, not the rest of the map (a green line otherwise carries forward).
            double restoreSv = velocityBefore(patternEnd(velocityById) + 1);

            bool any = false;
            foreach (int id in ids)
            {
                var o = parsed.HitObjects.First(h => h.Id == id);
                double desired = Math.Clamp(velocityById[id] / smTarget, 0.1, 10);
                if (Math.Abs(desired - velocityAt(o.StartTime)) < 1e-4)
                    continue; // the map already runs at the right SV here - no green line needed

                applySliderVelocityLine(o.StartTime, desired);
                parsed.RebuildTimingDerived(); // so the next slider's velocityAt sees this line
                recomputeSliderDurationsData(); // durations under the new SV, so the end position below is accurate
                any = true;
            }

            // Drop a reset line at the *true* pattern end - measured after the green lines above changed the slider
            // durations (a faster SV shortens the slider, moving its tail earlier), so it lands on the real tail.
            int resetTime = (int)Math.Round(patternEnd(velocityById));
            if (any && Math.Abs(velocityAt(resetTime) - restoreSv) > 1e-4)
            {
                applySliderVelocityLine(resetTime, restoreSv);
                parsed.RebuildTimingDerived();
                recomputeSliderDurationsData();
            }
        }

        /// <summary>The currently-selected objects (time-ordered) - the source for saving a Pattern Gallery pattern.</summary>
        private IReadOnlyList<HitObjectModel> CapturePattern()
        {
            var ids = new HashSet<int>(selection.Selected);
            return parsed.HitObjects.Where(o => ids.Contains(o.Id)).OrderBy(o => o.StartTime).ToList();
        }

        /// <summary>Saves the current selection to the user's Pattern Gallery (server-side), then refreshes it.</summary>
        private void saveSelectionAsPattern()
        {
            var objs = CapturePattern();
            if (objs.Count == 0)
                return;

            string? token = auth?.Token;
            if (token == null)
            {
                // Not logged in: tell the user and open the gallery, which shows the log-in prompt.
                toasts?.Push("Log in to save patterns", EditorTheme.Colours.Warning);
                patternGallery.Show();
                return;
            }

            // Capture each slider's source velocity (SliderMultiplier · effective SV at its time) so a paste into a
            // map with a different tempo / slider multiplier can reproduce the same rhythmic length via green lines.
            double smSource = parsed.SliderMultiplier;
            double? sliderVelocity(HitObjectModel o) =>
                o.Kind == HitObjectKind.Slider ? smSource * velocityAt(o.StartTime) : (double?)null;

            // The source tempo at the pattern start, so a paste into a different-BPM map can rescale the spacing.
            double sourceBeatLength = beatLengthAt(objs.Min(o => o.StartTime));

            string content = PatternSerializer.Serialize(objs, sliderVelocity, sourceBeatLength);
            int count = objs.Count;

            // Save locally first so the pattern shows up instantly, then upload in the background and reconcile its id.
            if (auth?.User.Value?.Id is long uid)
                patternStore?.EnsureUser(uid);
            var local = patternStore?.AddLocal("Pattern", null, content, count);
            patternGallery.RefreshFromCache();
            toasts?.Push("Pattern saved", EditorTheme.Colours.Velocity);

            System.Threading.Tasks.Task.Run(async () =>
            {
                var saved = await Online.SobeApi.CreatePatternAsync(token, "Pattern", null, content, count).ConfigureAwait(false);
                Schedule(() =>
                {
                    if (saved is { } serverId && local != null)
                        patternStore?.ConfirmUpload(local.Id, serverId);
                    else if (saved == null)
                        toasts?.Push("Pattern saved locally (couldn't reach server)", EditorTheme.Colours.Warning);
                    patternGallery.RefreshFromCache();
                });
            });
        }

        // --- Undo / redo: snapshots of the hit-object list ---

        private Snapshot takeSnapshot() =>
            new Snapshot(
                new List<HitObjectModel>(parsed.HitObjects),
                new List<TimingPointModel>(parsed.TimingPointModels),
                reviewDoc.Annotations.ConvertAll(a => a.Clone()));

        private void pushUndo()
        {
            undoStack.Push(takeSnapshot());
            redoStack.Clear();
        }

        private void undo()
        {
            if (undoStack.Count == 0)
                return;

            redoStack.Push(takeSnapshot());
            restore(undoStack.Pop());
        }

        private void redo()
        {
            if (redoStack.Count == 0)
                return;

            undoStack.Push(takeSnapshot());
            restore(redoStack.Pop());
        }

        /// <summary>Adds a bookmark at the current time (deduped, kept sorted), then refreshes the timelines.</summary>
        private void addBookmark()
        {
            int time = (int)Math.Round(CurrentTime);

            // Ignore a near-duplicate (a bookmark already within the snap window of this time).
            if (parsed.Bookmarks.Any(b => Math.Abs(b - time) <= 2))
                return;

            parsed.Bookmarks.Add(time);
            parsed.Bookmarks.Sort();
            bookmarksChanged();
        }

        /// <summary>Removes the bookmark nearest the current time, within a small time window.</summary>
        private void removeNearestBookmark()
        {
            const double window_ms = 100;
            int time = (int)Math.Round(CurrentTime);

            int nearest = -1;
            double best = window_ms;
            foreach (int b in parsed.Bookmarks)
            {
                double d = Math.Abs(b - time);
                if (d <= best)
                {
                    best = d;
                    nearest = b;
                }
            }

            if (nearest < 0)
                return;

            parsed.Bookmarks.Remove(nearest);
            bookmarksChanged();
        }

        private void bookmarksChanged()
        {
            topTimeline.Rebuild();
            bottomTimeline.Rebuild();
            editable.IsDirty.Value = true;
        }

        private void restore(Snapshot snapshot)
        {
            parsed.HitObjects.Clear();
            parsed.HitObjects.AddRange(snapshot.Objects);

            parsed.TimingPointModels.Clear();
            parsed.TimingPointModels.AddRange(snapshot.TimingPoints);
            parsed.RebuildTimingDerived();

            // Restore the Review layer too (deep-copied so the live doc never shares state with the stack).
            reviewDoc.Annotations.Clear();
            reviewDoc.Annotations.AddRange(snapshot.Annotations.ConvertAll(a => a.Clone()));
            refreshAnnotations();

            selection.Clear();
            applyStacking();
            rebuildHitObjects();
            rebuildSampleEvents();
            topTimeline.Rebuild();
            bottomTimeline.Rebuild();
            editable.IsDirty.Value = true;
        }

        /// <summary>
        /// Recomputes combo numbers/colours across the (time-ordered) object list from each object's
        /// new-combo flag - the same derivation the decoder uses, so numbering stays correct after edits.
        /// </summary>
        private void recomputeCombos() => applyCombosTo(parsed.HitObjects);

        /// <summary>Derives each object's combo number/colour index from the new-combo flags, in time order.</summary>
        private static void applyCombosTo(List<HitObjectModel> list)
        {
            int comboNumber = 0;
            int comboIndex = 0;
            bool first = true;

            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                int type = rawType(o.RawLine);
                bool newCombo = (type & 0b100) != 0;

                comboNumber = newCombo ? 1 : comboNumber + 1;
                if (newCombo && !first)
                    comboIndex += 1 + ((type >> 4) & 0b111);
                first = false;

                list[i] = o with { ComboNumber = comboNumber, ComboIndex = comboIndex };
            }
        }

        private static int rawType(string rawLine)
        {
            string[] parts = rawLine.Split(',');
            return parts.Length >= 4 && int.TryParse(parts[3], out int type) ? type : 0;
        }

        private Track? loadTrack(AudioManager audio, GameHost host)
        {
            if (parsed.AudioFilename.Length == 0
                || !set.Files.TryGetValue(parsed.AudioFilename.ToLowerInvariant(), out string? hash))
                return null;

            string? audioPath = LazerFileStore.ResolvePath(set.DataDirectory, hash);
            if (audioPath == null)
                return null;

            var storage = new NativeStorage(Path.GetDirectoryName(audioPath)!, host);
            trackStore = audio.GetTrackStore(new StorageBackedResourceStore(storage));
            return trackStore.Get(Path.GetFileName(audioPath));
        }

        private Texture? loadBackgroundTexture(GameHost host)
        {
            if (parsed.BackgroundFilename.Length == 0
                || !set.Files.TryGetValue(parsed.BackgroundFilename.ToLowerInvariant(), out string? hash))
                return null;

            string? bgPath = LazerFileStore.ResolvePath(set.DataDirectory, hash);
            if (bgPath == null)
                return null;

            var storage = new NativeStorage(Path.GetDirectoryName(bgPath)!, host);
            textures = new LargeTextureStore(host.Renderer, host.CreateTextureLoaderStore(new StorageBackedResourceStore(storage)));
            return textures.Get(Path.GetFileName(bgPath));
        }

        /// <summary>
        /// The editor backdrop: the song background image with an adjustable dim, over a solid base for when
        /// the map has no background. The <see cref="BackgroundToggleButton"/> dim wheel drives the dim.
        /// </summary>
        private Drawable buildBackground(GameHost host)
        {
            var layers = new Container { RelativeSizeAxes = Axes.Both };

            // Solid base so transparency never shows through (and the backdrop when there's no image).
            layers.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark });

            if (backgroundTexture != null)
            {
                layers.Add(new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    FillMode = FillMode.Fill,
                    Texture = backgroundTexture,
                });

                var dim = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black };
                settings.BackgroundDim.BindValueChanged(v => dim.Alpha = v.NewValue, true);
                layers.Add(dim);
            }

            return layers;
        }

        // Small Song Setup / Settings buttons tucked into the top-left, just below the top timeline.
        // Exit is via the configured exit key.
        private FillFlowContainer buildToolButtons() => new FillFlowContainer
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            // Ordered "configure the map -> configure the editor -> output", and all uniform icon buttons so the
            // row reads as a consistent toolbar (matching the icon-button row just below it).
            Children = new Drawable[]
            {
                new UI.IconBarButton(osu.Framework.Graphics.Sprites.FontAwesome.Solid.Music, "Song setup - this map's metadata, difficulty (CS/AR/OD) and colours",
                    () => songSettingsOverlay.ToggleVisibility()),
                new UI.IconBarButton(osu.Framework.Graphics.Sprites.FontAwesome.Solid.Cog, "Settings - editor preferences",
                    () => settingsOverlay.ToggleVisibility()),
                exportButton = new UI.IconBarButton(osu.Framework.Graphics.Sprites.FontAwesome.Solid.FileExport, "Export... (.osz set or this difficulty's .osu)",
                    () => exportMenu.ShowAt(exportButton.ScreenSpaceDrawQuad.BottomLeft)),
            },
        };

        /// <summary>Hides/shows the editor chrome (timelines, panels, chips, counters); the playfield recentres and
        /// grows slightly into the freed space (eased in <see cref="updateHitsoundLayout"/>). Shift+Tab / eye button.</summary>
        private void toggleHud()
        {
            hudHidden = !hudHidden;
            hudLayer.FadeTo(hudHidden ? 0 : 1, 200, Easing.OutQuint);
            if (hudHidden)
                toasts?.Push("Interface hidden - Shift+Tab to show", icon: osu.Framework.Graphics.Sprites.FontAwesome.Solid.EyeSlash);
        }

        /// <summary>Exports the open map's set to a <c>.osz</c> off-thread, then toasts + reveals it. Reflects the
        /// last saved state of the set's files (unsaved edits aren't included until you save).</summary>
        private void exportMap()
        {
            toasts?.Push($"Exporting {set.Artist} - {set.Title}...", icon: osu.Framework.Graphics.Sprites.FontAwesome.Solid.FileExport);
            string exportsDir = host.Storage.GetFullPath("exports");
            var exportSet = set;

            Task.Run(() =>
            {
                string? error = BeatmapArchiveExporter.Export(exportSet, exportsDir, out string outputPath);
                Schedule(() =>
                {
                    if (error == null)
                    {
                        toasts?.Push($"Exported to {Path.GetFileName(outputPath)}", EditorTheme.Colours.Success);
                        host.PresentFileExternally(outputPath);
                    }
                    else
                    {
                        toasts?.Push(error, EditorTheme.Colours.Error);
                    }
                });
            });
        }

        /// <summary>Exports just the open difficulty's <c>.osu</c> (last saved state) off-thread, then toasts + reveals it.</summary>
        private void exportDifficultyOsu()
        {
            toasts?.Push($"Exporting [{difficulty.DifficultyName}]...", icon: osu.Framework.Graphics.Sprites.FontAwesome.Solid.FileExport);
            string exportsDir = host.Storage.GetFullPath("exports");
            var exportSet = set;
            var exportDiff = difficulty;

            Task.Run(() =>
            {
                string? error = BeatmapArchiveExporter.ExportDifficultyOsu(exportSet, exportDiff, exportsDir, out string outputPath);
                Schedule(() =>
                {
                    if (error == null)
                    {
                        toasts?.Push($"Exported to {Path.GetFileName(outputPath)}", EditorTheme.Colours.Success);
                        host.PresentFileExternally(outputPath);
                    }
                    else
                    {
                        toasts?.Push(error, EditorTheme.Colours.Error);
                    }
                });
            });
        }

        // --- Authorship colouring (who placed what, from the collab's revision history) ---

        private void toggleAuthorship()
        {
            if (authorshipBusy)
                return;

            if (authorshipOn.Value)
            {
                // Turn off: drop the tint and the legend.
                authorshipOn.Value = false;
                authorship = null;
                authorColours.Clear();
                playfield.SetAuthorColouring(null);
                updateAuthorLegend();
                return;
            }

            var link = collabs?.Get(statisticsKey);
            string? token = auth?.Token;
            if (link == null || token == null)
            {
                toasts?.Push("Authorship needs this map linked to a collab.");
                return;
            }

            authorshipBusy = true;
            toasts?.Push("Loading authorship...");
            Guid collabId = link.CollabId;
            Task.Run(async () =>
            {
                var built = await Online.CollabAuthorship.BuildAsync(token, collabId).ConfigureAwait(false);
                Schedule(() =>
                {
                    authorshipBusy = false;
                    if (built == null)
                    {
                        toasts?.Push("No revision history to colour by yet.");
                        return;
                    }

                    authorship = built;
                    assignAuthorColours();
                    authorshipOn.Value = true;
                    playfield.SetAuthorColouring(authorColourForObject);
                    updateAuthorLegend();
                });
            });
        }

        private Color4? authorColourForObject(HitObjectModel o)
        {
            if (authorship?.AuthorAt(o.StartTime) is long id && authorColours.TryGetValue(id, out var c))
                return c;
            return null; // unknown placement -> fall back to the combo colour
        }

        private void assignAuthorColours()
        {
            authorColours.Clear();
            if (authorship == null)
                return;

            int i = 0;
            foreach (var (id, _) in authorship.Authors)
                authorColours[id] = author_palette[i++ % author_palette.Length];
        }

        private void updateAuthorLegend()
        {
            authorLegend.Clear();

            if (!authorshipOn.Value || authorship == null)
            {
                authorLegend.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                return;
            }

            authorLegend.Add(new SpriteText
            {
                Text = "PLACED BY",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(),
            });

            foreach (var (id, name) in authorship.Authors)
            {
                var colour = authorColours.TryGetValue(id, out var c) ? c : Color4.White;
                authorLegend.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new Circle
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(10),
                            Colour = colour,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = name,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                });
            }

            authorLegend.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rebuildHitObjects();

            // Light the COLLAB chip if this difficulty is already linked to a server collab.
            collabLinked.Value = collabs?.IsLinked(statisticsKey) == true;

            if (track != null && parsed.HitObjects.Count > 0)
                track.Seek(Math.Max(0, parsed.HitObjects[0].StartTime - 200));

            // Greet the user with the beta notice unless they've opted out.
            if (settings.ShowBetaPopup.Value)
                betaOverlay.Show();

            // Push the power-saving preference into the framework's global frame-sync, and keep it in sync.
            settings.PowerSaving.BindValueChanged(_ => applyFrameLimit(), true);
        }

        /// <summary>
        /// Applies the power-saving preference to the framework's global frame limiter: VSync (cap to the
        /// monitor's refresh) when on, the default 2x-refresh when off. The framework persists this itself, so
        /// it stays in effect after leaving the editor.
        /// </summary>
        private void applyFrameLimit()
        {
            var frameSync = frameworkConfig?.GetBindable<osu.Framework.Configuration.FrameSync>(
                osu.Framework.Configuration.FrameworkSetting.FrameSync);
            if (frameSync == null)
                return;

            frameSync.Value = settings.PowerSaving.Value
                ? osu.Framework.Configuration.FrameSync.VSync
                : osu.Framework.Configuration.FrameSync.Limit2x;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // The platform "command" modifier: Cmd on macOS, Ctrl elsewhere (matching osu!lazer).
            bool cmd = Shortcut.CommandPressed(e);

            // While the (non-intrusive) slider-to-stream preview is live, Enter commits and Escape cancels;
            // every other key is swallowed so an edit can't corrupt the transient preview state.
            if (streamOriginal != null)
            {
                if (e.Key is Key.Enter or Key.KeypadEnter)
                    sliderToStreamOverlay.Commit();
                else if (e.Key == Key.Escape)
                    sliderToStreamOverlay.Hide();
                return true;
            }

            // Overlay toggles are handled first so the same key both opens AND closes its menu - and pressing
            // another menu's key while one is open switches straight to it.
            if (settings.TimingPointsKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                toggleEditorOverlay(timingPointsOverlay);
                return true;
            }

            if (settings.SongSetupKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                toggleEditorOverlay(songSettingsOverlay);
                return true;
            }

            if (settings.SettingsKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                toggleEditorOverlay(settingsOverlay);
                return true;
            }

            if (settings.PatternGalleryKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                patternGallery.ToggleVisibility();
                return true;
            }

            // Ctrl+Shift+M toggles Modding Mode (a mode, not a modal overlay).
            if (settings.ModdingModeKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                moddingMode.Value = !moddingMode.Value;
                return true;
            }

            // Ctrl+Shift+A toggles Review mode (the modding-annotation layer).
            if (settings.ReviewModeKey.Value.Matches(e) && !e.Repeat && !confirmExit.State.Value.Equals(Visibility.Visible))
            {
                reviewMode.Value = !reviewMode.Value;
                return true;
            }

            // Let open dialogs handle their own keys.
            if (anyOverlayOpen())
                return base.OnKeyDown(e);

            if (settings.PlayPauseKey.Value.Matches(e) && !e.Repeat)
            {
                togglePlay();
                return true;
            }

            if (settings.ExitKey.Value.Matches(e))
            {
                // Escape backs out: cancel an in-progress slider/spinner placement, then placement tools,
                // selection, exit.
                if (playfield.BuildingSlider)
                    playfield.CancelSliderBuild();
                else if (spinnerBuilding)
                    playfield.CancelSpinnerBuild();
                else if (playfield.PlacementActive || playfield.SliderPlacementActive || playfield.SpinnerPlacementActive)
                {
                    playfield.SetPlacementActive(false);
                    playfield.SetSliderPlacementActive(false);
                    playfield.SetSpinnerPlacementActive(false);
                }
                else if (selection.Selected.Count > 0)
                    selection.Clear();
                else
                    tryExit();
                return true;
            }

            if ((e.Key == Key.Delete || e.Key == Key.BackSpace) && !e.Repeat)
            {
                DeleteSelected();
                return true;
            }

            // Ctrl+S saves in place (updates the carousel without leaving the editor).
            if (cmd && e.Key == Key.S && !e.Repeat)
            {
                save();
                return true;
            }

            // Add a timing point at the playhead: Ctrl/Cmd+Shift+P a green SV line, Ctrl/Cmd+P a red BPM line.
            if (settings.AddSvPointKey.Value.Matches(e) && !e.Repeat)
            {
                AddTimingPointAt(CurrentTime, uninherited: false);
                return true;
            }

            if (settings.AddBpmPointKey.Value.Matches(e) && !e.Repeat)
            {
                AddTimingPointAt(CurrentTime, uninherited: true);
                return true;
            }

            // Ctrl+A selects every object in the map, like lazer.
            if (cmd && !e.Repeat && e.Key == Key.A)
            {
                selection.SetRange(parsed.HitObjects.Select(o => o.Id));
                return true;
            }

            // Copy / paste HITSOUNDS only (Ctrl+Shift+C / Ctrl+Shift+V): the additions/banks/volume/index "feel".
            // Placed before the plain Ctrl+C/V so the Shift variants win instead of falling through to object copy.
            if (cmd && e.ShiftPressed && !e.Repeat && e.Key == Key.C)
            {
                CopyHitsounds();
                return true;
            }

            if (cmd && e.ShiftPressed && !e.Repeat && e.Key == Key.V)
            {
                PasteHitsounds();
                return true;
            }

            // Copy / cut / paste (Ctrl+C / Ctrl+X / Ctrl+V), like lazer.
            if (cmd && !e.Repeat && e.Key == Key.C)
            {
                copySelection();
                return true;
            }

            if (cmd && !e.Repeat && e.Key == Key.X)
            {
                cutSelection();
                return true;
            }

            if (cmd && !e.Repeat && e.Key == Key.V)
            {
                paste();
                return true;
            }

            // Bookmarks: Ctrl+B adds one at the current time, Ctrl+Shift+B removes the nearest.
            if (cmd && !e.Repeat && e.Key == Key.B)
            {
                if (e.ShiftPressed)
                    removeNearestBookmark();
                else
                    addBookmark();
                return true;
            }

            // Undo / redo (Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y), like lazer.
            if (cmd && !e.Repeat && (e.Key == Key.Z || e.Key == Key.Y))
            {
                if (e.Key == Key.Y || e.ShiftPressed)
                    redo();
                else
                    undo();
                return true;
            }

            // Z / X jump the playhead to the first / last hit object (no modifier).
            if (!cmd && !e.Repeat && (e.Key == Key.Z || e.Key == Key.X) && parsed.HitObjects.Count > 0)
            {
                seekTo(e.Key == Key.Z
                    ? parsed.HitObjects.Min(o => o.StartTime)
                    : parsed.HitObjects.Max(o => o.StartTime));
                return true;
            }

            // While reviewing, the number keys pick the Review tools (1 select, 2 note, 3 line) instead of the
            // composing tools (which are suppressed in Review mode anyway).
            if (reviewMode.Value && !e.Repeat && e.Key is Key.Number1 or Key.Number2 or Key.Number3)
            {
                applyReviewTool(e.Key switch { Key.Number2 => ReviewTool.Note, Key.Number3 => ReviewTool.Draw, _ => ReviewTool.Select });
                return true;
            }

            // Tools: (1) select, (2) circle, (3) slider, (4) spinner - matching osu!lazer's toolbox shortcuts.
            // Routed through applyTool so each one fully disarms the others (e.g. 1 also clears the spinner).
            if (e.Key == Key.Number1 && !e.Repeat)
            {
                applyTool(EditorTool.Selection);
                return true;
            }

            if (e.Key == Key.Number2 && !e.Repeat)
            {
                applyTool(EditorTool.Circle);
                return true;
            }

            if (e.Key == Key.Number3 && !e.Repeat)
            {
                applyTool(EditorTool.Slider);
                return true;
            }

            if (e.Key == Key.Number4 && !e.Repeat)
            {
                applyTool(EditorTool.Spinner);
                return true;
            }

            // (Q) toggles new combo: on the selection, or armed for the next placed circle.
            if (e.Key == Key.Q && !e.Repeat)
            {
                toggleNewCombo();
                return true;
            }

            // Ctrl+G reverses the selected pattern (order + slider direction), like osu!lazer.
            if (cmd && e.Key == Key.G && !e.Repeat)
            {
                ReverseSelection();
                return true;
            }

            // Flip the selection: Ctrl+H horizontally, Ctrl+J vertically (lazer defaults).
            if (cmd && !e.Repeat && (e.Key == Key.H || e.Key == Key.J))
            {
                FlipSelection(e.Key == Key.H);
                return true;
            }

            // Ctrl+Shift+R opens the rotate-by-angle dialog (acts on the current selection).
            if (cmd && e.ShiftPressed && e.Key == Key.R && !e.Repeat)
            {
                if (selection.Selected.Count > 0)
                    rotationPopover.Show();
                return true;
            }

            // Ctrl/Cmd+. rotates the selection 90 deg clockwise, Ctrl/Cmd+, 90 deg counter-clockwise (around the
            // selection's centre, matching the Ctrl+scroll rotate). Shift is reserved for the playback-speed binding below.
            if (cmd && !e.ShiftPressed && !e.Repeat && (e.Key == Key.Comma || e.Key == Key.Period))
            {
                RotateSelectionAnimated(e.Key == Key.Period ? 90f : -90f, aroundPlayfieldCentre: false);
                return true;
            }

            // Playback speed: Ctrl+Shift+< slower, Ctrl+Shift+> faster (osu!lazer has no default binding for this).
            if (cmd && e.ShiftPressed && (e.Key == Key.Comma || e.Key == Key.Period))
            {
                if (e.Key == Key.Period)
                    playbackControl.IncreaseRate();
                else
                    playbackControl.DecreaseRate();
                return true;
            }

            // G (no modifier) cycles the grid size, like lazer's EditorCycleGridSpacing.
            if (e.Key == Key.G && !e.Repeat && !cmd)
            {
                playfield.CycleGridSize();
                return true;
            }

            // Shift+Tab hides/shows the whole editor HUD (osu!'s "hide interface" convention), leaving only the
            // background/grid + hit objects. The shortcut is how you bring the HUD back, since the toggle button
            // is itself part of the hidden HUD.
            if (e.Key == Key.Tab && e.ShiftPressed && !cmd && !e.Repeat)
            {
                toggleHud();
                return true;
            }

            // Toggle distance snapping (lazer default Y).
            if (settings.DistanceSnapKey.Value.Matches(e) && !e.Repeat)
            {
                distanceSnapEnabled = !distanceSnapEnabled;
                return true;
            }

            // Toggle the hitsound-lanes editor (default H).
            if (settings.HitsoundsKey.Value.Matches(e) && !e.Repeat)
            {
                hitsoundMode.Value = !hitsoundMode.Value;
                return true;
            }

            // Convert the selected slider into a stream (default Ctrl+Shift+F).
            if (settings.ConvertStreamKey.Value.Matches(e) && !e.Repeat)
            {
                convertSelectedSliderToStream();
                return true;
            }

            // Hitsound additions on the selection (or the pending placement defaults), matching osu!lazer:
            // W whistle, E finish, R clap; with Shift they set the normal sample bank instead (Shift+W normal,
            // Shift+E soft, Shift+R drum).
            if (!cmd && !e.Repeat && e.Key is Key.W or Key.E or Key.R)
            {
                if (e.ShiftPressed)
                    SetNormalBank(e.Key switch { Key.W => SampleBank.Normal, Key.E => SampleBank.Soft, _ => SampleBank.Drum });
                else
                    ToggleAddition(e.Key switch { Key.W => 0b0010, Key.E => 0b0100, _ => 0b1000 });
                return true;
            }

            return base.OnKeyDown(e);
        }

        private bool anyOverlayOpen() =>
            settingsOverlay.State.Value == Visibility.Visible
            || songSettingsOverlay.State.Value == Visibility.Visible
            || timingPointsOverlay.State.Value == Visibility.Visible
            || confirmExit.State.Value == Visibility.Visible
            || rotationPopover.State.Value == Visibility.Visible
            || noteEditPopover.State.Value == Visibility.Visible
            || timingPillPopover.State.Value == Visibility.Visible
            || collabOverlay.State.Value == Visibility.Visible;

        /// <summary>Opens the inline timing-point editor beneath a clicked timeline pill.</summary>
        private void onTimingPillClicked(int id, Vector2 screenPosition)
        {
            int idx = parsed.TimingPointModels.FindIndex(tp => tp.Id == id);
            if (idx < 0)
                return;

            timingPillPopover.OpenFor(parsed.TimingPointModels[idx], timingPillPopover.ToLocalSpace(screenPosition));
        }

        /// <summary>
        /// Toggles an editor overlay: closes it if it's open, otherwise closes any other open editor overlay
        /// and shows it - so pressing F5 while the F6 menu is open switches straight to song setup.
        /// </summary>
        private void toggleEditorOverlay(VisibilityContainer overlay)
        {
            bool wasOpen = overlay.State.Value == Visibility.Visible;

            settingsOverlay.Hide();
            songSettingsOverlay.Hide();
            timingPointsOverlay.Hide();

            if (!wasOpen)
                overlay.Show();
        }

        private void togglePlay()
        {
            if (track == null)
                return;

            if (track.IsRunning)
                track.Stop();
            else
            {
                if (track.HasCompleted)
                    track.Seek(0);
                track.Start();
            }
        }

        private void tryExit()
        {
            if (editable.IsDirty.Value)
                confirmExit.Show();
            else
                this.Exit();
        }

        /// <summary>Snapshots the editable metadata/difficulty fields into the saver's edit set.</summary>
        private BeatmapSaver.Edits buildEdits() => new BeatmapSaver.Edits
        {
            Title = editable.Title.Value,
            TitleUnicode = editable.TitleUnicode.Value,
            Artist = editable.Artist.Value,
            ArtistUnicode = editable.ArtistUnicode.Value,
            Creator = editable.Creator.Value,
            Version = editable.Version.Value,
            Source = editable.Source.Value,
            Tags = editable.Tags.Value,
            Hp = editable.Hp.Value,
            Cs = editable.Cs.Value,
            Ar = editable.Ar.Value,
            Od = editable.Od.Value,
            StackLeniency = editable.StackLeniency.Value,
            SliderMultiplier = editable.SliderMultiplier.Value,
            SliderTickRate = editable.SliderTickRate.Value,
            ComboColours = editable.MapColours.Select(c => c.Value).ToList(),
        };

        private bool save()
        {
            var edits = buildEdits();

            // Compute a fresh star rating synchronously so the value written to the realm reflects the exact
            // saved state (the live chip is debounced and might be a beat behind). Cheap relative to the I/O.
            currentStars = StarRatingCalculator.Calculate(snapshotForStars());
            edits.StarRating = currentStars;
            starChip.Child = new StarRatingDisplay(currentStars);

            // Existing maps are written in place directly in lazer's realm (exactly how lazer's own editor
            // saves) - going through the .osz importer would make lazer file the edit as a duplicate set.
            // Brand-new maps (no stored .osu yet) still round-trip through the importer.
            bool ok;
            if (!string.IsNullOrEmpty(difficulty.OsuFileHash))
            {
                string? error = BeatmapRealmWriter.Save(set, difficulty, parsed, edits);
                ok = error == null;
                if (!ok)
                    toasts?.Push($"Save failed: {error}", EditorTheme.Colours.Error);
            }
            else
            {
                ok = BeatmapSaver.Save(set, difficulty, parsed, edits);
                if (!ok)
                    toasts?.Push("Save failed", EditorTheme.Colours.Error);
            }

            if (ok)
            {
                editable.IsDirty.Value = false;
                DidSave = true;
                // Persist the Review layer alongside the map (the .osu re-hashes on save; the layer is keyed by a
                // stable per-difficulty key, and stamps the new hash so an export reflects the saved version).
                saveReviewLayer();
                toasts?.Push("Beatmap saved", EditorTheme.Colours.Success, osu.Framework.Graphics.Sprites.FontAwesome.Solid.Save);

                // Saving no longer auto-pushes a collab revision - the mapper uploads progress on demand from the
                // COLLAB panel, so every Ctrl+S doesn't spawn a new revision. Nudge them once if it's a collab.
                if (collabs?.Get(statisticsKey) != null)
                    toasts?.Push("Collab: use \"Upload progress\" to share this", EditorTheme.Colours.Info);
            }
            return ok;
        }

        /// <summary>
        /// Uploads the current map state to the collab as a new fast-forward revision (driven on demand by the
        /// "Upload progress" button, not by every save). A 409 means a partner pushed first, so the user must
        /// pull before uploading again. Returns (ok, message) for the COLLAB panel.
        /// </summary>
        private async Task<(bool ok, string message)> pushCollabProgressAsync()
        {
            var link = collabs?.Get(statisticsKey);
            string? token = auth?.Token;
            if (link == null || token == null)
                return (false, "This difficulty isn't a collab.");

            // Build the .osu from the current in-memory state (what the mapper sees), off the update thread.
            var edits = buildEdits();
            Guid collabId = link.CollabId;
            int baseRevision = link.BaseRevision;
            string key = statisticsKey;

            string? osu = await Task.Run(() => BeatmapSaver.BuildPatchedOsu(set, difficulty, parsed, edits)).ConfigureAwait(false);
            if (osu == null)
                return (false, "Couldn't build the map.");

            var result = await Online.SobeApi.PushRevisionAsync(token, collabId, baseRevision, osu, null).ConfigureAwait(false);
            if (result.Ok)
            {
                Schedule(() => collabs?.SetBaseRevision(key, result.Number));
                return (true, result.NoOp ? "Already up to date - nothing to upload." : $"Uploaded progress (revision {result.Number}).");
            }
            if (result.Conflict)
                return (false, "Your partner pushed first - pull their changes before uploading.");
            return (false, "Upload failed.");
        }

        /// <summary>
        /// Starts a collab for the open difficulty: creates it on the server, pushes the current .osu as the
        /// first revision, and uploads the audio/background so a collaborator can bootstrap. Links the diff
        /// locally on success. Returns (ok, message) for the panel.
        /// </summary>
        private async Task<(bool ok, string message)> startCollabAsync()
        {
            string? token = auth?.Token;
            if (token == null)
                return (false, "Not logged in.");
            if (collabs == null)
                return (false, "Collab unavailable.");
            if (collabs.IsLinked(statisticsKey))
                return (false, "This difficulty is already a collab.");

            string? osu = BeatmapSaver.BuildPatchedOsu(set, difficulty, parsed, buildEdits());
            if (osu == null)
                return (false, "Couldn't read this difficulty's .osu.");

            string title = $"{set.Artist} - {set.Title} [{difficulty.DifficultyName}]";
            long? onlineId = set.OnlineID > 0 ? set.OnlineID : null;

            Guid? created = await Online.SobeApi.CreateCollabAsync(token, title, onlineId).ConfigureAwait(false);
            if (created is not Guid collabId)
                return (false, "Couldn't create the collab.");

            var push = await Online.SobeApi.PushRevisionAsync(token, collabId, 0, osu, "Initial").ConfigureAwait(false);
            if (!push.Ok)
                return (false, "Couldn't upload the map.");

            // The diff references these by filename; upload so a new collaborator can bootstrap the set.
            await uploadCollabAssetAsync(token, collabId, "audio", firstFilename(parsed.AudioFilename, difficulty.AudioFile)).ConfigureAwait(false);
            await uploadCollabAssetAsync(token, collabId, "background", firstFilename(parsed.BackgroundFilename, difficulty.BackgroundFile)).ConfigureAwait(false);

            int rev = push.Number;
            string key = statisticsKey;
            Schedule(() =>
            {
                collabs.Link(key, collabId, rev);
                collabLinked.Value = true;
            });

            return (true, "Collab started. Add a collaborator below.");
        }

        /// <summary>Adds a collaborator to the open diff's collab by osu! username.</summary>
        private async Task<(bool ok, string message)> addCollabMemberAsync(string username)
        {
            string? token = auth?.Token;
            var link = collabs?.Get(statisticsKey);
            if (token == null || link == null)
                return (false, "This difficulty isn't a collab.");

            bool ok = await Online.SobeApi.AddMemberAsync(token, link.CollabId, username).ConfigureAwait(false);
            return ok ? (true, $"Invited {username}. They'll join once they accept.") : (false, "No sobe user with that username.");
        }

        private async Task uploadCollabAssetAsync(string token, Guid collabId, string kind, string? filename)
        {
            if (string.IsNullOrEmpty(filename))
                return;

            byte[]? bytes = readSetFile(filename);
            if (bytes == null)
                return;

            await Online.SobeApi.UploadAssetAsync(token, collabId, kind, filename, bytes).ConfigureAwait(false);
        }

        /// <summary>The first non-empty filename, e.g. the .osu's audio field falling back to realm metadata.</summary>
        private static string? firstFilename(string a, string b) =>
            !string.IsNullOrEmpty(a) ? a : (!string.IsNullOrEmpty(b) ? b : null);

        /// <summary>Reads a file from this set's lazer file store by its referenced filename, or null if missing.</summary>
        private byte[]? readSetFile(string filename)
        {
            try
            {
                if (string.IsNullOrEmpty(set.DataDirectory)
                    || !set.Files.TryGetValue(filename.ToLowerInvariant(), out string? hash)
                    || string.IsNullOrEmpty(hash))
                    return null;

                string? path = LazerFileStore.ResolvePath(set.DataDirectory, hash);
                return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            bool cmd = Shortcut.CommandPressed(e);

            // Ctrl/Cmd+scroll rotates the current selection around its centre (5 deg per notch); with nothing
            // selected it falls back to changing the beat-snap divisor (finer when scrolling up), like lazer.
            if (cmd)
            {
                if (selection.Selected.Count > 0)
                {
                    RotateSelectionBy(e.ScrollDelta.Y > 0 ? -5f : 5f, aroundPlayfieldCentre: false);
                    return true;
                }

                beatDivisor.Step(e.ScrollDelta.Y > 0 ? 1 : -1);
                return true;
            }

            // Alt+scroll adjusts the distance-snap spacing multiplier, like lazer.
            if (e.AltPressed)
            {
                distanceSpacing = Math.Clamp(distanceSpacing + (e.ScrollDelta.Y > 0 ? 0.1 : -0.1), 0.1, 4.0);
                return true;
            }

            if (e.ShiftPressed || track == null)
                return false;

            // osu!lazer convention: scroll up = earlier, down = later. One notch = one beat-snap division.
            int notches = Math.Max(1, (int)Math.Round(Math.Abs(e.ScrollDelta.Y)));
            int direction = e.ScrollDelta.Y > 0 ? -1 : 1;

            // While playing, the playhead keeps advancing during the seek, so a plain one-division step lands
            // almost where we started (or even nets forward when seeking back). Lazer's EditorClock adds 1.5
            // divisions to the amount whenever the clock is running, so a mid-playback seek actually moves.
            double amount = notches + (track.IsRunning ? 1.5 : 0);

            seekBeatSnapped(direction, amount);
            return true;
        }

        /// <summary>
        /// Beat-snapped seek, ported directly from osu!lazer's <c>EditorClock.seek</c>: project one beat-snap
        /// division in the seek direction, snap that to the beat grid of the active timing section, clamp a
        /// forward seek to the next timing point, and - if rounding landed us back on the current beat - step
        /// one more division so a seek always moves. Reference time is the track's accurate position.
        /// </summary>
        private void seekBeatSnapped(int direction, double amount)
        {
            if (track == null || amount <= 0)
                return;

            double current = track.CurrentTime;

            var tp = beatPointAt(current);

            // Going backwards while sitting exactly on a timing point: snap within the *previous* section.
            if (direction < 0 && Math.Abs(tp.Time - current) < 0.5 && tp.Time > 0)
                tp = beatPointAt(current - 1);

            // The snap grid is exactly one beat division; `amount` only scales the projection distance (it can
            // be fractional - e.g. 2.5 while playing - so it must NOT widen the grid, or we'd snap off-beat).
            double beatLength = tp.BeatLength / beatDivisor.Value.Value;
            if (beatLength <= 0)
                return;

            double seekTime = current + beatLength * amount * direction;

            // Snap the projected time to the nearest beat of this section, biased in the seek direction.
            double rel = seekTime - tp.Time;
            int closestBeat = direction > 0 ? (int)Math.Floor(rel / beatLength) : (int)Math.Ceiling(rel / beatLength);
            seekTime = tp.Time + closestBeat * beatLength;

            // A forward seek can't cross into the next timing section; clamp to its start.
            double? nextTime = nextBeatPointTime(tp.Time);
            if (direction > 0 && nextTime.HasValue && seekTime > nextTime.Value)
                seekTime = nextTime.Value;

            // Rounding can land us back on the current beat (a no-op); push one more division so we always move.
            if (Math.Abs(current - seekTime) < 0.5)
                seekTime = tp.Time + (closestBeat + direction) * beatLength;

            // Never fall before this section's start (unless it's the very first one).
            if (seekTime < tp.Time && tp.Time > (parsed.BeatPoints.Count > 0 ? parsed.BeatPoints[0].Time : 0))
                seekTime = tp.Time;

            seekTo(seekTime);
        }

        /// <summary>The uninherited beat point active at the given time (the last one at/before it, or the first).</summary>
        private BeatPoint beatPointAt(double time)
        {
            if (parsed.BeatPoints.Count == 0)
                return new BeatPoint(0, 500, 4); // 120 BPM fallback

            var point = parsed.BeatPoints[0];
            foreach (var p in parsed.BeatPoints)
            {
                if (p.Time <= time)
                    point = p;
                else
                    break;
            }
            return point;
        }

        /// <summary>The time of the first uninherited beat point after <paramref name="afterTime"/>, if any.</summary>
        private double? nextBeatPointTime(double afterTime)
        {
            foreach (var p in parsed.BeatPoints)
            {
                if (p.Time > afterTime)
                    return p.Time;
            }
            return null;
        }

        /// <summary>
        /// Seeks the track to <paramref name="time"/> (clamped to the track) and immediately resyncs the
        /// interpolating clock, so a seek mid-playback lands cleanly instead of stuttering as the
        /// interpolation catches up - the behaviour osu!lazer's decoupled editor clock gives for free.
        /// </summary>
        private void seekTo(double time)
        {
            if (track == null)
                return;

            track.Seek(Math.Clamp(time, 0, track.Length));
            advanceClocks();
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            if (host.Window != null)
                host.Window.Title = $"{set.Artist} - {set.Title} [{difficulty.DifficultyName}]";

            this.FadeInFromZero(300, Easing.OutQuint);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            statistics?.ExitMap();
            LastTime = track?.CurrentTime ?? 0;
            track?.Stop();

            // Leaving the editor: restore the hard idle cap (we may have raised it for playback).
            host.MaximumInactiveHz = inactive_hz_idle;

            if (host.Window != null)
                host.Window.Title = "osu! Beatmap Editor";

            this.FadeOut(200, Easing.OutQuint);
            return base.OnExiting(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (reviewDragDropHandler != null && host?.Window != null)
                host.Window.DragDrop -= reviewDragDropHandler;
            stopAllLoops();
            track?.Dispose();
            trackStore?.Dispose();
            textures?.Dispose();
            base.Dispose(isDisposing);
        }

        /// <summary>A small "..." chip next to the AU mod button that opens the Auto-cursor settings popover on demand.</summary>
        private partial class MenuDotsButton : osu.Framework.Graphics.Containers.Container
        {
            private readonly Action onClick;
            private Box background = null!;

            public MenuDotsButton(Action onClick)
            {
                this.onClick = onClick;

                Size = new Vector2(24, 30);
                Masking = true;
                CornerRadius = 7;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface },
                    new osu.Framework.Graphics.Sprites.SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Y = -3,
                        Text = "...",
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(OsuColour.BackgroundRaised, 120);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => background.FadeColour(OsuColour.Surface, 120);

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }
    }
}
