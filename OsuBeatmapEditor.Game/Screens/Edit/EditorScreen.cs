using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private sealed record Snapshot(List<HitObjectModel> Objects, List<TimingPointModel> TimingPoints);

        private readonly Stack<Snapshot> undoStack = new Stack<Snapshot>();
        private readonly Stack<Snapshot> redoStack = new Stack<Snapshot>();
        private ParsedBeatmap parsed = new ParsedBeatmap();

        private Playfield playfield = null!;
        private Container composeContainer = null!;
        private TopTimeline topTimeline = null!;

        // Toggles the expanded hitsound-lanes editor in the top timeline (the playfield shrinks to make room).
        private readonly BindableBool hitsoundMode = new BindableBool();
        // Smoothly-interpolated current top-bar height, lerped toward its collapsed/expanded target each frame.
        private float topHeightCurrent = top_bar_height;
        private SpriteText bpmText = null!;
        private SpriteText svText = null!;
        private SpriteText distanceSnapText = null!;
        private BeatDivisorControl beatDivisorControl = null!;
        private FillFlowContainer leftPanels = null!;
        private HitsoundBankBar hitsoundBankBar = null!;
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
        private TimingPillPopover timingPillPopover = null!;
        private PlaybackControl playbackControl = null!;

        private GameHost host = null!;
        private ITrackStore? trackStore;
        private Track? track;
        private InterpolatingFramedClock? audioClock;
        private HitsoundPlayer? hitsounds;
        private int hitsoundIndex;
        private double lastHitsoundTime;
        private bool needHitsoundResync = true;

        /// <summary>What a scheduled <see cref="SampleEvent"/> plays.</summary>
        private enum SampleEventKind { Hit, SliderTick }

        /// <summary>A single scheduled hitsound playback: object heads, each slider node, spinner ends, and slider ticks.</summary>
        private readonly record struct SampleEvent(double Time, SampleEventKind Kind, int HitSound, SampleBank Normal, SampleBank Addition, float Volume);

        /// <summary>All hitsound events for the map, time-sorted; rebuilt after every edit (see <see cref="rebuildSampleEvents"/>).</summary>
        private readonly List<SampleEvent> sampleEvents = new List<SampleEvent>();

        /// <summary>The hitsounds applied to newly placed objects (set from the left-panel palette when nothing is selected).</summary>
        private int pendingHitSound;
        // New objects default to Auto banks (inherit the timing point), like osu!lazer.
        private SampleBank pendingNormalBank = SampleBank.Auto;
        private SampleBank pendingAdditionBank = SampleBank.Auto;
        private LargeTextureStore? textures;
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
            }

            // Hitsound feedback: bundled default-skin samples, played as playback crosses each object.
            var sampleStore = audio.GetSampleStore(
                new NamespacedResourceStore<byte[]>(new DllResourceStore(OsuBeatmapEditorResources.ResourceAssembly), "Samples"));
            hitsounds = new HitsoundPlayer(sampleStore);
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
                topTimeline = new TopTimeline(parsed, () => CurrentTime, track?.Length ?? 0, hitsoundMode)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    TimingPillClicked = onTimingPillClicked,
                },
                toolButtons = buildToolButtons(),
                bottomTimeline = new EditorTimeline(track, parsed, () => CurrentTime, rightInset: PlaybackControl.WIDTH) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
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
                new HitsoundModeButton(hitsoundMode)
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Left = 12, Bottom = bottom_bar_height + 12 + 30 + 8 },
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
                // Beat divisor sits on top, with the BPM and SV readouts stacked below it.
                beatDivisorControl = new BeatDivisorControl
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 8 },
                },
                bpmText = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 46 },
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Title(numeric: true),
                },
                svText = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 70 },
                    Colour = EditorTheme.Colours.Velocity,
                    Font = EditorTheme.Type.Title(numeric: true),
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
                        new HitsoundPanel
                        {
                            StateProvider = CurrentHitsoundState,
                            ToggleAddition = ToggleAddition,
                            SetNormalBank = SetNormalBank,
                            SetAdditionBank = SetAdditionBank,
                        },
                    },
                },
                hitsoundBankBar = new HitsoundBankBar
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Alpha = 0,
                    StateProvider = CurrentHitsoundState,
                    SetNormalBank = SetNormalBank,
                    SetAdditionBank = SetAdditionBank,
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
                timingPillPopover = new TimingPillPopover
                {
                    OnApply = UpdateTimingPoint,
                    OnDelete = DeleteTimingPoint,
                },
                // Frontmost so the beta notice sits above all editor chrome when shown on open.
                betaOverlay = new BetaNoticeOverlay(),
            };

            playfield.TimeSource = () => CurrentTime;
            playfield.SnappedTimeSource = () => snapTime(CurrentTime);
            playfield.PlacementSnap = SnapPlacement;
            playfield.SliderTickDistance = sliderTickDistance;

            // Hide the left tool/hitsound column while the lanes editor is open (it owns hitsound editing now),
            // and show the lazer-style bank bar instead.
            hitsoundMode.BindValueChanged(e =>
            {
                leftPanels.FadeTo(e.NewValue ? 0 : 1, 150, Easing.OutQuad);
                hitsoundBankBar.FadeTo(e.NewValue ? 1 : 0, 150, Easing.OutQuad);
            }, true);

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

            // Hit objects animate against the interpolated audio clock (set in load), so transforms track playback.
            if (audioClock != null)
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
        }

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

        protected override void Update()
        {
            base.Update();
            updateHitsoundLayout();
            // Advance the interpolating clock once per frame, before children read CurrentTime.
            audioClock?.ProcessFrame();
            updateHitsounds();
            updateBpm();
            toolPanel.SetActive(currentTool());

            distanceSnapText.Alpha = distanceSnapEnabled ? 1 : 0;
            if (distanceSnapEnabled && distanceSpacing != lastDistanceSpacing)
            {
                lastDistanceSpacing = distanceSpacing;
                distanceSnapText.Text = $"Distance snap: {distanceSpacing:0.0}x";
            }

            updatePlacementComboPreview();

            if (spinnerBuilding)
                updateSpinnerPreview();
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

            if (Math.Abs(topHeightCurrent - target) < 0.5f)
                topHeightCurrent = target;
            else
                topHeightCurrent = (float)Interpolation.Lerp(target, topHeightCurrent, Math.Exp(-0.018 * Time.Elapsed));

            topTimeline.Height = topHeightCurrent;
            composeContainer.Padding = new MarginPadding { Top = topHeightCurrent, Bottom = bottom_bar_height };

            // Keep the top-right HUD (beat divisor on top, then BPM / SV / distance-snap) just below the timeline as it grows.
            beatDivisorControl.Margin = new MarginPadding { Right = 16, Top = topHeightCurrent + 8 };
            bpmText.Margin = new MarginPadding { Right = 16, Top = topHeightCurrent + 46 };
            svText.Margin = new MarginPadding { Right = 16, Top = topHeightCurrent + 70 };
            distanceSnapText.Margin = new MarginPadding { Right = 16, Top = topHeightCurrent + 96 };

            // The Song Setup / Settings buttons slide down with the timeline so they stay just below it.
            toolButtons.Margin = new MarginPadding { Left = 12, Top = topHeightCurrent + 6 };

            // The lazer-style bank bar sits below the tool buttons (which are ~24px tall) on the left.
            hitsoundBankBar.Margin = new MarginPadding { Left = 12, Top = topHeightCurrent + 40 };
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

            playfield.SetHitObjects(temp, circleDiameter(), ParsedBeatmap.PreemptFor(editable.Ar.Value));
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
        private double lastSv = double.NaN;

        /// <summary>Shows the BPM of the timing section under the current playback position.</summary>
        private void updateBpm()
        {
            double now = CurrentTime;
            double beatLength = 0;

            foreach (var p in parsed.BeatPoints)
            {
                if (p.Time <= now)
                    beatLength = p.BeatLength;
                else
                    break;
            }

            if (beatLength <= 0 && parsed.BeatPoints.Count > 0)
                beatLength = parsed.BeatPoints[0].BeatLength;

            if (beatLength != lastBpmBeatLength)
            {
                lastBpmBeatLength = beatLength;
                bpmText.Text = beatLength > 0 ? $"{60000.0 / beatLength:0.##} BPM" : string.Empty;
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
                        hitsounds.PlaySliderTick(e.Normal, e.Volume);
                    else
                        hitsounds.Play(e.HitSound, e.Normal, e.Addition, e.Volume);
                    hitsoundIndex++;
                }
            }

            lastHitsoundTime = now;
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
                        sampleEvents.Add(new SampleEvent(t, SampleEventKind.Hit, o.HitSound, n, a, eventVolume(o, t)));
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
                            sampleEvents.Add(new SampleEvent(t, SampleEventKind.Hit, ns.HitSound, n, a, eventVolume(o, t)));
                        }
                        addSliderTicks(o);
                        break;

                    default:
                    {
                        var (n, a) = resolveBanksAt(o.StartTime, o.NormalBank, o.AdditionBank);
                        sampleEvents.Add(new SampleEvent(o.StartTime, SampleEventKind.Hit, o.HitSound, n, a, eventVolume(o, o.StartTime)));
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
                    // The slider tick plays in the slider's resolved normal bank.
                    SampleBank tickBank = resolveBanksAt(t, o.NormalBank, o.AdditionBank).Normal;
                    sampleEvents.Add(new SampleEvent(t, SampleEventKind.SliderTick, 0, tickBank, SampleBank.Normal, eventVolume(o, t)));
                }
            }
        }

        private void rebuildHitObjects()
        {
            playfield.SetHitObjects(parsed.HitObjects, circleDiameter(), ParsedBeatmap.PreemptFor(editable.Ar.Value));
        }

        /// <summary>
        /// Hit-circle diameter in osu!pixels for the current CS (standard formula <c>(54.4 - 4.48·CS)·2</c>),
        /// minus a manual visual override so our circles match osu!lazer's editor, which renders them a few
        /// pixels smaller than the raw formula gives.
        /// </summary>
        private float circleDiameter() => (54.4f - 4.48f * editable.Cs.Value) * 2 - circle_diameter_override;

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
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            // Leave the new object unselected: keeping it selected made Q (new combo) act on the freshly
            // placed circle instead of arming the next placement, which surprised the mapper.
            selection.Clear();
        }

        /// <summary>Inserts a new slider through the given control points (head first) on the current snapped time.</summary>
        public void PlaceSlider(IReadOnlyList<SliderControlPoint> points)
        {
            var cps = SliderGeometry.InferSegmentTypes(clampControlPoints(points));
            if (cps.Count < 2)
                return;

            int time = (int)Math.Round(snapTime(CurrentTime));

            // The freshly-traced slider spans its full control polygon; snap that length so the tail lands on a tick.
            double pixelLength = snapSliderLength(time, SliderGeometry.PathLength(SliderGeometry.ComputePath(cps)));
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
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            // Leave the new slider unselected (see PlaceCircle) so Q keeps arming the next placement.
            selection.Clear();
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
                HitSound: pendingHitSound, NormalBank: pendingNormalBank, AdditionBank: pendingAdditionBank));
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


        public void PreviewSliderPlacement(IReadOnlyList<SliderControlPoint> points)
        {
            int time = (int)Math.Round(snapTime(CurrentTime));
            previewSliderTimeline(time, points, snap: true);
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

            // Never snap PAST the drawn curve: extending the expected distance beyond the path length makes the
            // tail shoot off in a straight line (lazer's calculateLength extends linearly). Step down a tick so
            // the slider always ends on the curve, at a tick. (A sub-one-tick curve is the only unavoidable case.)
            if (velocity * snapped > pixelLength)
                snapped -= tickMs;

            return velocity * Math.Max(tickMs, snapped);
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
            double sv = 1;
            foreach (var p in parsed.VelocityPoints)
            {
                if (p.Time <= time)
                    sv = p.Multiplier;
                else
                    break;
            }
            return sv;
        }

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
            return Math.Max(60, span * slides);
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

        /// <summary>A snapshot of the hitsounds shown by the palette: the selection's, or the pending placement defaults.</summary>
        public readonly record struct HitsoundState(int HitSound, SampleBank Normal, SampleBank Addition, bool HasSelection);

        /// <summary>The hitsounds the palette should display: the selected slider node's, else the first selected object's, else the pending defaults.</summary>
        public HitsoundState CurrentHitsoundState()
        {
            if (nodeSelection.Selected is { } node && tryNodeSample(node, out NodeSample ns))
                return new HitsoundState(ns.HitSound, ns.NormalBank, ns.AdditionBank, true);

            foreach (var o in parsed.HitObjects)
            {
                if (selection.Contains(o.Id))
                    return new HitsoundState(o.HitSound, o.NormalBank, o.AdditionBank, true);
            }

            return new HitsoundState(pendingHitSound, pendingNormalBank, pendingAdditionBank, false);
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

        /// <summary>The pending placement hitSample field: "normalSet:additionSet:index:volume:filename".</summary>
        private string pendingSampleField() => $"{pendingSet()}:0:0:";

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

        /// <summary>Plays a one-off hitsound so a palette change is immediately audible (osu!lazer feedback). Banks
        /// are resolved (Auto -> the timing point at <paramref name="time"/>) so the feedback matches playback.</summary>
        private void playFeedback(double time, int hitSound, SampleBank normal, SampleBank addition)
        {
            var (n, a) = resolveBanksAt(time, normal, addition);
            hitsounds?.Play(hitSound, n, a, 1f);
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

            // Move by the raw delta, like osu!lazer's OsuSelectionHandler.moveObjects: no magnetic snapping
            // onto other objects' centres - the selection simply follows the cursor, and stacking (below)
            // resolves any consecutive overlaps afterwards.
            Vector2 delta = new Vector2((float)Math.Round(rawDelta.X), (float)Math.Round(rawDelta.Y));

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
            if (tryNearestObjectPosition(pos, id => !playfield.IsObjectVisible(id) || selection.Contains(id), out Vector2 target, out float dist) && dist < magnetic_snap_radius)
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
        private bool tryNearestObjectPosition(Vector2 pos, Func<int, bool> isExcluded, out Vector2 nearest, out float distance)
        {
            nearest = pos;
            distance = float.MaxValue;
            bool found = false;

            foreach (var o in parsed.HitObjects)
            {
                if (isExcluded(o.Id))
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

        /// <summary>
        /// Converts the single selected slider into a stream of circles, one per beat-snap division along its
        /// duration, positioned at the matching point on the slider path (honouring repeats). The first circle
        /// inherits the slider's new-combo flag. Mirrors osu!stable's "convert slider to stream".
        /// </summary>
        private void convertSelectedSliderToStream()
        {
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
            if (step <= 0)
                return;

            int count = Math.Clamp((int)Math.Round(slider.Duration / step), 1, 256);
            bool firstNewCombo = HitObjectLineEditor.HasNewCombo(slider.RawLine);
            double spanDuration = slider.Duration / Math.Max(1, slider.Slides);

            pushUndo();
            parsed.HitObjects.RemoveAt(idx);
            selection.Clear();

            int newId = nextId();
            var added = new List<int>();

            for (int i = 0; i <= count; i++)
            {
                double elapsed = i * step;
                if (elapsed > slider.Duration + 1)
                    break;

                // Map elapsed time to a position on the path, bouncing back and forth across repeats.
                int span = spanDuration > 0 ? (int)(elapsed / spanDuration) : 0;
                double within = spanDuration > 0 ? (elapsed - span * spanDuration) / spanDuration : 0;
                double frac = span % 2 == 0 ? within : 1 - within;
                Vector2 pos = samplePath(slider.Path, Math.Clamp(frac, 0, 1));

                int px = Math.Clamp((int)Math.Round(pos.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH);
                int py = Math.Clamp((int)Math.Round(pos.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT);
                int t = (int)Math.Round(slider.StartTime + elapsed);
                int type = i == 0 && firstNewCombo ? 0b101 : 0b001;
                string raw = $"{px},{py},{t},{type},0,0:0:0:0:";

                parsed.HitObjects.Add(new HitObjectModel(px, py, t, HitObjectKind.Circle, null, RawLine: raw, Id: newId));
                added.Add(newId);
                newId++;
            }

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            afterEdit();
            selection.SetRange(added);
        }

        /// <summary>Samples a polyline at a fraction (0..1) of its total length.</summary>
        private static Vector2 samplePath(IReadOnlyList<Vector2> path, double fraction)
        {
            if (path.Count == 1)
                return path[0];

            double total = 0;
            for (int i = 1; i < path.Count; i++)
                total += (path[i] - path[i - 1]).Length;

            if (total <= 0)
                return path[0];

            double target = fraction * total;
            double acc = 0;
            for (int i = 1; i < path.Count; i++)
            {
                double seg = (path[i] - path[i - 1]).Length;
                if (acc + seg >= target)
                {
                    float f = seg > 0 ? (float)((target - acc) / seg) : 0;
                    return path[i - 1] + (path[i] - path[i - 1]) * f;
                }
                acc += seg;
            }

            return path[^1];
        }

        // --- Timing points ---

        public int AddTimingPoint(TimingPointModel point)
        {
            pushUndo();
            int id = nextTimingPointId();
            parsed.TimingPointModels.Add(point with { Id = id });
            afterTimingEdit();
            return id;
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
            applySelectionMap(p => rotateAround(p, centre, degrees));
        }

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

            transformUndo = takeSnapshot();
            transformChanged = false;

            applySelectionMap(p => rotateAround(p, centre, degrees));

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
            int startTime = sel.Min(o => o.StartTime);
            int endTime = sel.Max(o => o.StartTime + (int)Math.Round(o.Duration));
            var newComboOrder = sel.Select(o => (rawType(o.RawLine) & 0b100) != 0).ToList();

            pushUndo();

            var updated = new Dictionary<int, HitObjectModel>();
            foreach (var o in sel)
            {
                var n = o;

                if (many)
                {
                    int objEnd = o.StartTime + (int)Math.Round(o.Duration);
                    int newStart = endTime - (objEnd - startTime);
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

                double pixelLength = SliderGeometry.PathLength(SliderGeometry.ComputePath(cps));
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

            var clip = HitObjectClipboard.Objects;
            int target = (int)Math.Round(snapTime(CurrentTime));
            int baseTime = clip.Min(o => o.StartTime);
            int offset = target - baseTime;

            pushUndo();

            int id = nextId();
            var newIds = new List<int>(clip.Count);
            foreach (var o in clip)
            {
                newIds.Add(id);
                parsed.HitObjects.Add(o with
                {
                    StartTime = o.StartTime + offset,
                    RawLine = HitObjectLineEditor.ShiftTime(o.RawLine, offset),
                    Id = id,
                });
                id++;
            }

            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            // Pasted sliders carry their source map's duration; re-time them for this map's SV/BPM.
            recomputeSliderDurationsData();
            afterEdit();
            selection.SetRange(newIds);
        }

        // --- Undo / redo: snapshots of the hit-object list ---

        private Snapshot takeSnapshot() =>
            new Snapshot(new List<HitObjectModel>(parsed.HitObjects), new List<TimingPointModel>(parsed.TimingPointModels));

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
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Margin = new MarginPadding { Left = 12, Top = top_bar_height + 6 },
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new OsuButton("Song Setup", OsuColour.Surface)
                {
                    Size = new Vector2(86, 24),
                    FontSize = 13,
                    CornerRadius = 5,
                    Action = () => songSettingsOverlay.ToggleVisibility(),
                },
                new OsuButton("Settings", OsuColour.Surface)
                {
                    Size = new Vector2(72, 24),
                    FontSize = 13,
                    CornerRadius = 5,
                    Action = () => settingsOverlay.ToggleVisibility(),
                },
            },
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            rebuildHitObjects();

            if (track != null && parsed.HitObjects.Count > 0)
                track.Seek(Math.Max(0, parsed.HitObjects[0].StartTime - 200));

            // Greet the user with the beta notice unless they've opted out.
            if (settings.ShowBetaPopup.Value)
                betaOverlay.Show();
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
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
            if (e.ControlPressed && e.Key == Key.S && !e.Repeat)
            {
                save();
                return true;
            }

            // Ctrl+A selects every object in the map, like lazer.
            if (e.ControlPressed && !e.Repeat && e.Key == Key.A)
            {
                selection.SetRange(parsed.HitObjects.Select(o => o.Id));
                return true;
            }

            // Copy / cut / paste (Ctrl+C / Ctrl+X / Ctrl+V), like lazer.
            if (e.ControlPressed && !e.Repeat && e.Key == Key.C)
            {
                copySelection();
                return true;
            }

            if (e.ControlPressed && !e.Repeat && e.Key == Key.X)
            {
                cutSelection();
                return true;
            }

            if (e.ControlPressed && !e.Repeat && e.Key == Key.V)
            {
                paste();
                return true;
            }

            // Bookmarks: Ctrl+B adds one at the current time, Ctrl+Shift+B removes the nearest.
            if (e.ControlPressed && !e.Repeat && e.Key == Key.B)
            {
                if (e.ShiftPressed)
                    removeNearestBookmark();
                else
                    addBookmark();
                return true;
            }

            // Undo / redo (Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y), like lazer.
            if (e.ControlPressed && !e.Repeat && (e.Key == Key.Z || e.Key == Key.Y))
            {
                if (e.Key == Key.Y || e.ShiftPressed)
                    redo();
                else
                    undo();
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
            if (e.ControlPressed && e.Key == Key.G && !e.Repeat)
            {
                ReverseSelection();
                return true;
            }

            // Flip the selection: Ctrl+H horizontally, Ctrl+J vertically (lazer defaults).
            if (e.ControlPressed && !e.Repeat && (e.Key == Key.H || e.Key == Key.J))
            {
                FlipSelection(e.Key == Key.H);
                return true;
            }

            // Ctrl+Shift+R opens the rotate-by-angle dialog (acts on the current selection).
            if (e.ControlPressed && e.ShiftPressed && e.Key == Key.R && !e.Repeat)
            {
                if (selection.Selected.Count > 0)
                    rotationPopover.Show();
                return true;
            }

            // Playback speed: Ctrl+Shift+< slower, Ctrl+Shift+> faster (osu!lazer has no default binding for this).
            if (e.ControlPressed && e.ShiftPressed && (e.Key == Key.Comma || e.Key == Key.Period))
            {
                if (e.Key == Key.Period)
                    playbackControl.IncreaseRate();
                else
                    playbackControl.DecreaseRate();
                return true;
            }

            // G (no modifier) cycles the grid size, like lazer's EditorCycleGridSpacing.
            if (e.Key == Key.G && !e.Repeat && !e.ControlPressed)
            {
                playfield.CycleGridSize();
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

            return base.OnKeyDown(e);
        }

        private bool anyOverlayOpen() =>
            settingsOverlay.State.Value == Visibility.Visible
            || songSettingsOverlay.State.Value == Visibility.Visible
            || timingPointsOverlay.State.Value == Visibility.Visible
            || confirmExit.State.Value == Visibility.Visible
            || rotationPopover.State.Value == Visibility.Visible
            || timingPillPopover.State.Value == Visibility.Visible;

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

        private bool save()
        {
            var edits = new BeatmapSaver.Edits
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
                toasts?.Push("Beatmap saved", EditorTheme.Colours.Success);
            }
            return ok;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            // Ctrl+scroll changes the beat-snap divisor (finer when scrolling up), like lazer.
            if (e.ControlPressed)
            {
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

            seekBeatSnapped(direction, notches);
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

            double seekAmount = tp.BeatLength / beatDivisor.Value.Value * amount;
            if (seekAmount <= 0)
                return;

            double seekTime = current + seekAmount * direction;

            // Snap the projected time to the nearest beat of this section, biased in the seek direction.
            double rel = seekTime - tp.Time;
            int closestBeat = direction > 0 ? (int)Math.Floor(rel / seekAmount) : (int)Math.Ceiling(rel / seekAmount);
            seekTime = tp.Time + closestBeat * seekAmount;

            // A forward seek can't cross into the next timing section; clamp to its start.
            double? nextTime = nextBeatPointTime(tp.Time);
            if (direction > 0 && nextTime.HasValue && seekTime > nextTime.Value)
                seekTime = nextTime.Value;

            // Rounding can land us back on the current beat (a no-op); push one more division so we always move.
            if (Math.Abs(current - seekTime) < 0.5)
                seekTime = tp.Time + (closestBeat + direction) * seekAmount;

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
            audioClock?.ProcessFrame();
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

            if (host.Window != null)
                host.Window.Title = "osu! Beatmap Editor";

            this.FadeOut(200, Easing.OutQuint);
            return base.OnExiting(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            track?.Dispose();
            trackStore?.Dispose();
            textures?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
