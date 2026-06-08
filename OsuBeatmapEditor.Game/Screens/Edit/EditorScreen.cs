using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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

        private readonly BeatmapSetModel set;
        private readonly BeatmapDifficultyModel difficulty;

        private EditorSettings settings = null!;
        private EditableBeatmap editable = null!;
        private readonly EditorSelection selection = new EditorSelection();
        private readonly BeatSnapDivisor beatDivisor = new BeatSnapDivisor();
        private readonly Dictionary<int, HitObjectModel> moveSnapshot = new Dictionary<int, HitObjectModel>();
        private int moveTimeDelta;
        private Vector2 movePosDelta;
        private Vector2 moveMin, moveMax;

        private readonly Stack<List<HitObjectModel>> undoStack = new Stack<List<HitObjectModel>>();
        private readonly Stack<List<HitObjectModel>> redoStack = new Stack<List<HitObjectModel>>();
        private readonly List<HitObjectModel> clipboard = new List<HitObjectModel>();
        private ParsedBeatmap parsed = new ParsedBeatmap();

        private Playfield playfield = null!;
        private TopTimeline topTimeline = null!;
        private SpriteText bpmText = null!;
        private SpriteText svText = null!;
        private ToolPanel toolPanel = null!;
        private EditorSettingsOverlay settingsOverlay = null!;
        private SongSettingsOverlay songSettingsOverlay = null!;
        private ConfirmExitOverlay confirmExit = null!;

        private GameHost host = null!;
        private ITrackStore? trackStore;
        private Track? track;
        private InterpolatingFramedClock? audioClock;
        private HitsoundPlayer? hitsounds;
        private int hitsoundIndex;
        private double lastHitsoundTime;
        private bool needHitsoundResync = true;
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

            editable = new EditableBeatmap(parsed, settings.DefaultCreator.Value);

            deps.CacheAs(settings);
            deps.CacheAs(editable);
            deps.CacheAs(selection);
            deps.CacheAs(beatDivisor);
            deps.CacheAs<IEditorActions>(this);
            return deps;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio, GameHost host)
        {
            this.host = host;
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

            InternalChildren = new Drawable[]
            {
                buildBackground(host),
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = top_bar_height, Bottom = bottom_bar_height },
                    Child = playfield = new Playfield(),
                },
                topTimeline = new TopTimeline(parsed, () => CurrentTime, track?.Length ?? 0)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                },
                buildToolButtons(),
                new EditorTimeline(track, parsed, () => CurrentTime) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
                new BackgroundToggleButton(settings.UseSongBackground, settings.BackgroundDim, backgroundTexture != null)
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
                bpmText = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 8 },
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 18, weight: "Bold"),
                },
                svText = new SpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 30 },
                    Colour = new Color4(0.30f, 0.82f, 0.40f, 1f),
                    Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                },
                new BeatDivisorControl
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Right = 16, Top = top_bar_height + 54 },
                },
                toolPanel = new ToolPanel
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 12 },
                    ToolSelected = applyTool,
                },
                settingsOverlay = new EditorSettingsOverlay(),
                songSettingsOverlay = new SongSettingsOverlay(),
                confirmExit = new ConfirmExitOverlay
                {
                    OnSave = () => { save(); this.Exit(); },
                    OnDiscard = this.Exit,
                },
            };

            playfield.TimeSource = () => CurrentTime;
            playfield.SnappedTimeSource = () => snapTime(CurrentTime);

            // Live difficulty: AR changes the approach window (and the stack window); CS rebuilds (debounced).
            editable.Ar.BindValueChanged(v =>
            {
                playfield.Preempt = ParsedBeatmap.PreemptFor(v.NewValue);
                applyStacking();
                rebuildHitObjects();
            }, true);
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
        }

        /// <summary>The smoothed playback time (ms), interpolated between coarse audio-clock updates.</summary>
        private double CurrentTime => audioClock?.CurrentTime ?? track?.CurrentTime ?? 0;

        protected override void Update()
        {
            base.Update();
            // Advance the interpolating clock once per frame, before children read CurrentTime.
            audioClock?.ProcessFrame();
            updateHitsounds();
            updateBpm();
            toolPanel.SetActive(currentTool());
        }

        /// <summary>The composing tool the playfield currently has armed.</summary>
        private EditorTool currentTool() =>
            playfield.PlacementActive ? EditorTool.Circle
            : playfield.SliderPlacementActive ? EditorTool.Slider
            : EditorTool.Selection;

        /// <summary>Arms the chosen tool from the toolbox (Spinner is not placeable yet, so it is ignored).</summary>
        private void applyTool(EditorTool tool)
        {
            switch (tool)
            {
                case EditorTool.Selection:
                    playfield.SetPlacementActive(false);
                    playfield.SetSliderPlacementActive(false);
                    break;

                case EditorTool.Circle:
                    playfield.SetPlacementActive(true);
                    break;

                case EditorTool.Slider:
                    playfield.SetSliderPlacementActive(true);
                    break;

                case EditorTool.Spinner:
                    break; // not implemented yet
            }
        }

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

            bpmText.Text = beatLength > 0 ? $"{60000.0 / beatLength:0} BPM" : string.Empty;
            svText.Text = $"{velocityAt(now):0.##}x SV";
        }

        /// <summary>Plays each object's hitsounds as playback passes its start time (only while playing).</summary>
        private void updateHitsounds()
        {
            if (hitsounds == null || track == null)
                return;

            double now = CurrentTime;
            var objects = parsed.HitObjects;

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
                while (hitsoundIndex < objects.Count && objects[hitsoundIndex].StartTime <= now)
                    hitsoundIndex++;
                needHitsoundResync = false;
            }
            else
            {
                while (hitsoundIndex < objects.Count && objects[hitsoundIndex].StartTime <= now)
                {
                    hitsounds.Play(objects[hitsoundIndex]);
                    hitsoundIndex++;
                }
            }

            lastHitsoundTime = now;
        }

        private void rebuildHitObjects()
        {
            float diameter = (54.4f - 4.48f * editable.Cs.Value) * 2;
            playfield.SetHitObjects(parsed.HitObjects, diameter);
        }

        // --- IEditorActions: editing operations invoked by the timeline/playfield ---

        /// <summary>Inserts a new hit circle at the given osu!pixel position on the current snapped time.</summary>
        public void PlaceCircle(Vector2 osuPosition)
        {
            int time = (int)Math.Round(snapTime(CurrentTime));
            int x = Math.Clamp((int)Math.Round(osuPosition.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH);
            int y = Math.Clamp((int)Math.Round(osuPosition.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT);

            int id = nextId();

            // Type bit 0 = circle; bit 2 = new combo (set while Q is armed).
            int type = playfield.NewComboArmed ? 0b101 : 0b001;
            string raw = $"{x},{y},{time},{type},0,0:0:0:0:";

            pushUndo();
            removeObjectsAt(time);
            parsed.HitObjects.Add(new HitObjectModel(x, y, time, HitObjectKind.Circle, null, RawLine: raw, Id: id));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            selection.SetSingle(id);
        }

        /// <summary>Inserts a new slider through the given control anchors (head first) on the current snapped time.</summary>
        public void PlaceSlider(IReadOnlyList<SliderAnchor> points)
        {
            var anchors = clampAnchors(points);
            if (anchors.Count < 2)
                return;

            int time = (int)Math.Round(snapTime(CurrentTime));

            // Any sharp corner forces Bézier; otherwise infer from the anchor count (2=L, 3=P, 4+=B).
            char curveType = anchors.Any(a => a.Red) ? 'B' : SliderPathCalculator.DefaultCurveType(anchors.Count);

            // The freshly-traced slider spans its full control polygon; snap that length so the tail lands on a tick.
            double pixelLength = snapSliderLength(time, SliderGeometry.PathLength(SliderGeometry.ComputePath(anchors, curveType)));
            var fullPath = SliderGeometry.ComputePath(anchors, curveType, pixelLength);
            if (pixelLength < 1)
                return;

            int hx = (int)Math.Round(anchors[0].X);
            int hy = (int)Math.Round(anchors[0].Y);
            int id = nextId();

            // Type bit 1 = slider; bit 2 = new combo (set while Q is armed).
            int type = playfield.NewComboArmed ? 0b110 : 0b010;
            string length = pixelLength.ToString("0.###", CultureInfo.InvariantCulture);
            string curve = SliderGeometry.CurveField(curveType, anchors);
            // x,y,time,type,hitSound,sliderType|anchors...,slides,length,edgeSounds,edgeSets,hitSample
            string raw = $"{hx},{hy},{time},{type},0,{curve},1,{length},0|0,0:0|0:0,0:0:0:0:";

            double duration = sliderDuration(time, pixelLength, 1);

            pushUndo();
            removeObjectsAt(time);
            parsed.HitObjects.Add(new HitObjectModel(hx, hy, time, HitObjectKind.Slider, fullPath, duration, 1,
                RawLine: raw, Id: id, Anchors: anchors, CurveType: curveType));
            parsed.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            afterEdit();
            selection.SetSingle(id);
        }

        /// <summary>
        /// Rebuilds a slider from edited control points (move / add / delete / corner toggle). Like lazer, the
        /// slider length follows the new control polygon, snapped so its tail still lands on the beat grid.
        /// </summary>
        public void UpdateSliderAnchors(int id, IReadOnlyList<SliderAnchor> points)
        {
            int idx = parsed.HitObjects.FindIndex(o => o.Id == id);
            if (idx < 0 || parsed.HitObjects[idx].Kind != HitObjectKind.Slider)
                return;

            var o = parsed.HitObjects[idx];
            var anchors = clampAnchors(points);
            if (anchors.Count < 2)
                return;

            char curveType = SliderGeometry.AdjustType(o.CurveType, anchors);

            // The slider now spans its full control polygon; snap that length to the beat grid.
            double pixelLength = snapSliderLength(o.StartTime, SliderGeometry.PathLength(SliderGeometry.ComputePath(anchors, curveType)));
            var path = SliderGeometry.ComputePath(anchors, curveType, pixelLength);
            if (path.Count < 2 || pixelLength < 1)
                return;

            string raw = HitObjectLineEditor.SetSliderCurve(o.RawLine, curveType, anchors, pixelLength);
            double duration = sliderDuration(o.StartTime, pixelLength, o.Slides);

            pushUndo();
            parsed.HitObjects[idx] = o with
            {
                X = (int)Math.Round(anchors[0].X),
                Y = (int)Math.Round(anchors[0].Y),
                Path = path,
                Anchors = anchors,
                CurveType = curveType,
                Duration = duration,
                RawLine = raw,
            };
            afterEdit();
            selection.SetSingle(id);
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
            double snapped = Math.Max(tickMs, Math.Round(duration / tickMs) * tickMs);
            return velocity * snapped;
        }

        /// <summary>Clamps each control anchor to the playfield in integer osu!pixels (preserving red-corner flags).</summary>
        private static List<SliderAnchor> clampAnchors(IReadOnlyList<SliderAnchor> points)
        {
            var result = new List<SliderAnchor>(points.Count);
            foreach (var a in points)
            {
                result.Add(new SliderAnchor(
                    Math.Clamp((int)Math.Round(a.X), 0, (int)ParsedBeatmap.PLAYFIELD_WIDTH),
                    Math.Clamp((int)Math.Round(a.Y), 0, (int)ParsedBeatmap.PLAYFIELD_HEIGHT),
                    a.Red));
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

            // Clamp so the whole selection stays inside the playfield.
            float dx = Math.Clamp((float)Math.Round(rawDelta.X), -moveMin.X, ParsedBeatmap.PLAYFIELD_WIDTH - moveMax.X);
            float dy = Math.Clamp((float)Math.Round(rawDelta.Y), -moveMin.Y, ParsedBeatmap.PLAYFIELD_HEIGHT - moveMax.Y);

            movePosDelta = new Vector2(dx, dy);

            // Recompute stacking against the dragged positions so the selection visibly stacks onto objects it
            // overlaps - the same algorithm lazer's editor runs live while you move objects.
            playfield.PreviewPositionOffset(movePosDelta, liveStackHeights(movePosDelta));
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
                    Anchors = offsetAnchors(original.Anchors, dx, dy),
                    RawLine = HitObjectLineEditor.ShiftPosition(original.RawLine, dx, dy),
                };
            }
        }

        private static IReadOnlyList<SliderAnchor>? offsetAnchors(IReadOnlyList<SliderAnchor>? anchors, int dx, int dy)
        {
            if (anchors == null)
                return null;

            var result = new List<SliderAnchor>(anchors.Count);
            foreach (var a in anchors)
                result.Add(a with { X = a.X + dx, Y = a.Y + dy });
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

        /// <summary>Returns the beat-snapped time <paramref name="ticks"/> 1/4-beat steps from the given time.</summary>
        private double stepBeats(double timeMs, int ticks)
        {
            if (!tryActiveBeat(timeMs, out double pointTime, out double step))
                return timeMs + ticks * 100; // fallback if the map has no timing points

            const double eps = 1e-3;
            double rel = (timeMs - pointTime) / step;
            double k = ticks > 0 ? Math.Floor(rel + eps) + ticks : Math.Ceiling(rel - eps) + ticks;
            return pointTime + k * step;
        }

        /// <summary>Common post-edit refresh: renumber combos, restack, rebuild views, resync hitsounds, flag dirty.</summary>
        private void afterEdit()
        {
            recomputeCombos();
            applyStacking();
            rebuildHitObjects();
            topTimeline.Rebuild();
            needHitsoundResync = true;
            editable.IsDirty.Value = true;
        }

        /// <summary>Recomputes stack heights for the current AR window and stack leniency.</summary>
        private void applyStacking() =>
            StackingProcessor.Apply(parsed.HitObjects, ParsedBeatmap.PreemptFor(editable.Ar.Value), parsed.StackLeniency);

        // --- Copy / cut / paste: clipboard of hit objects, time-shifted on paste (lazer-style) ---

        /// <summary>Copies the selected objects (in time order) to the editor clipboard.</summary>
        private void copySelection()
        {
            if (selection.Selected.Count == 0)
                return;

            var ids = new HashSet<int>(selection.Selected);
            clipboard.Clear();
            // parsed.HitObjects is kept time-sorted, so the clipboard preserves relative timing.
            foreach (var o in parsed.HitObjects)
            {
                if (ids.Contains(o.Id))
                    clipboard.Add(o);
            }
        }

        private void cutSelection()
        {
            if (selection.Selected.Count == 0)
                return;

            copySelection();
            DeleteSelected();
        }

        /// <summary>Pastes the clipboard so its earliest object lands on the current snapped time, then selects it.</summary>
        private void paste()
        {
            if (clipboard.Count == 0)
                return;

            int target = (int)Math.Round(snapTime(CurrentTime));
            int baseTime = clipboard.Min(o => o.StartTime);
            int offset = target - baseTime;

            pushUndo();

            int id = nextId();
            var newIds = new List<int>(clipboard.Count);
            foreach (var o in clipboard)
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
            afterEdit();
            selection.SetRange(newIds);
        }

        // --- Undo / redo: snapshots of the hit-object list ---

        private void pushUndo()
        {
            undoStack.Push(new List<HitObjectModel>(parsed.HitObjects));
            redoStack.Clear();
        }

        private void undo()
        {
            if (undoStack.Count == 0)
                return;

            redoStack.Push(new List<HitObjectModel>(parsed.HitObjects));
            restore(undoStack.Pop());
        }

        private void redo()
        {
            if (redoStack.Count == 0)
                return;

            undoStack.Push(new List<HitObjectModel>(parsed.HitObjects));
            restore(redoStack.Pop());
        }

        private void restore(List<HitObjectModel> snapshot)
        {
            parsed.HitObjects.Clear();
            parsed.HitObjects.AddRange(snapshot);

            selection.Clear();
            applyStacking();
            rebuildHitObjects();
            topTimeline.Rebuild();
            needHitsoundResync = true;
            editable.IsDirty.Value = true;
        }

        /// <summary>
        /// Recomputes combo numbers/colours across the (time-ordered) object list from each object's
        /// new-combo flag - the same derivation the decoder uses, so numbering stays correct after edits.
        /// </summary>
        private void recomputeCombos()
        {
            int comboNumber = 0;
            int comboIndex = 0;
            bool first = true;

            for (int i = 0; i < parsed.HitObjects.Count; i++)
            {
                var o = parsed.HitObjects[i];
                int type = rawType(o.RawLine);
                bool newCombo = (type & 0b100) != 0;

                comboNumber = newCombo ? 1 : comboNumber + 1;
                if (newCombo && !first)
                    comboIndex += 1 + ((type >> 4) & 0b111);
                first = false;

                parsed.HitObjects[i] = o with { ComboNumber = comboNumber, ComboIndex = comboIndex };
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
        /// The editor backdrop: the song background image (with an adjustable dim) overlaid by a custom
        /// colour layer. The <see cref="BackgroundToggleButton"/> flips between them and drives the dim.
        /// </summary>
        private Drawable buildBackground(GameHost host)
        {
            var layers = new Container { RelativeSizeAxes = Axes.Both };

            // Solid base so transparency never shows through.
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

            // Custom-colour layer, shown when the song background is toggled off (or unavailable).
            var custom = new Box { RelativeSizeAxes = Axes.Both };
            settings.EditorBackgroundColour.BindValueChanged(v => custom.Colour = v.NewValue, true);
            layers.Add(custom);

            settings.UseSongBackground.BindValueChanged(v =>
            {
                bool song = v.NewValue && backgroundTexture != null;
                custom.FadeTo(song ? 0f : 1f, 200);
            }, true);

            return layers;
        }

        // Small Song Setup / Settings buttons tucked into the top-left, just below the top timeline.
        // Exit is via the configured exit key.
        private Drawable buildToolButtons() => new FillFlowContainer
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

            playfield.SetHitObjects(parsed.HitObjects, parsed.CircleRadius * 2);
            playfield.Preempt = parsed.Preempt;

            if (track != null && parsed.HitObjects.Count > 0)
                track.Seek(Math.Max(0, parsed.HitObjects[0].StartTime - 200));
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            // Let open dialogs handle their own keys.
            if (anyOverlayOpen())
                return base.OnKeyDown(e);

            if (e.Key == settings.PlayPauseKey.Value && !e.Repeat)
            {
                togglePlay();
                return true;
            }

            if (e.Key == settings.ExitKey.Value)
            {
                // Escape backs out: cancel an in-progress slider trace, then placement tools, selection, exit.
                if (playfield.BuildingSlider)
                    playfield.CancelSliderBuild();
                else if (playfield.PlacementActive || playfield.SliderPlacementActive)
                {
                    playfield.SetPlacementActive(false);
                    playfield.SetSliderPlacementActive(false);
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

            // Undo / redo (Ctrl+Z, Ctrl+Shift+Z or Ctrl+Y), like lazer.
            if (e.ControlPressed && !e.Repeat && (e.Key == Key.Z || e.Key == Key.Y))
            {
                if (e.Key == Key.Y || e.ShiftPressed)
                    redo();
                else
                    undo();
                return true;
            }

            // Tools: (1) select, (2) place hit circle, (3) place slider - matching osu!lazer's toolbox shortcuts.
            if (e.Key == Key.Number1 && !e.Repeat)
            {
                playfield.SetPlacementActive(false);
                playfield.SetSliderPlacementActive(false);
                return true;
            }

            if (e.Key == Key.Number2 && !e.Repeat)
            {
                playfield.SetPlacementActive(true);
                return true;
            }

            if (e.Key == Key.Number3 && !e.Repeat)
            {
                playfield.SetSliderPlacementActive(true);
                return true;
            }

            // (Q) toggles new combo: on the selection, or armed for the next placed circle.
            if (e.Key == Key.Q && !e.Repeat)
            {
                toggleNewCombo();
                return true;
            }

            if (e.Key == Key.G && !e.Repeat)
            {
                playfield.CycleGridSize();
                return true;
            }

            return base.OnKeyDown(e);
        }

        private bool anyOverlayOpen() =>
            settingsOverlay.State.Value == Visibility.Visible
            || songSettingsOverlay.State.Value == Visibility.Visible
            || confirmExit.State.Value == Visibility.Visible;

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
            };

            bool ok = BeatmapSaver.Save(set, difficulty, parsed, edits);
            if (ok)
            {
                editable.IsDirty.Value = false;
                DidSave = true;
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

            if (e.ShiftPressed || track == null)
                return false;

            // Step the playhead to the adjacent beat-snap tick. osu!lazer convention: up = earlier, down = later.
            int notches = Math.Max(1, (int)Math.Round(Math.Abs(e.ScrollDelta.Y)));
            int direction = e.ScrollDelta.Y > 0 ? -1 : 1;

            // Use the track's exact position (not the interpolated clock) so repeated notches step cleanly.
            double target = stepBeats(track.CurrentTime, direction * notches);
            track.Seek(Math.Clamp(target, 0, track.Length));
            return true;
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
