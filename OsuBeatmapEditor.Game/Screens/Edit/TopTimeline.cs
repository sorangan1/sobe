using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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

        [Resolved]
        private EditorSelection selection { get; set; } = null!;

        [Resolved]
        private IEditorActions actions { get; set; } = null!;

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private BeatSnapDivisor beatSnap { get; set; } = null!;

        /// <summary>The active beat-snap resolution (ticks per beat).</summary>
        private int divisor => beatSnap.Value.Value;

        private float pixelsPerMs = 0.4f;

        // Move-drag state (dragging an object shifts the selection in time).
        private bool moving;
        private int moveGrabbedId;
        private double moveStartTime;

        private Container content = null!;
        private Container timingLayer = null!;
        private Container gridLayer = null!;
        private Container objectLayer = null!;
        private Container selectionLayer = null!;
        private readonly List<Box> gridPool = new List<Box>();

        // Selection state, mirroring lazer: a shared selection + a live drag box.
        private readonly List<ObjBounds> objectBounds = new List<ObjBounds>();
        private readonly Dictionary<int, Container> blueprints = new Dictionary<int, Container>();
        private readonly Dictionary<int, float> blueprintBaseX = new Dictionary<int, float>();
        private Box? dragBox;
        private double dragStartTime;
        private HashSet<int> dragBaseline = new HashSet<int>();

        public TopTimeline(ParsedBeatmap beatmap, Func<double> currentTime, double trackLength)
        {
            this.beatmap = beatmap;
            this.currentTime = currentTime;
            this.trackLength = trackLength;

            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
            Masking = true;
        }

        /// <summary>Time extent (ms) of a hit object marker, with its stable id.</summary>
        private readonly record struct ObjBounds(int Id, double StartTime, double EndTime);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark },
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
                        // Timing-point lines (red = BPM, green = SV) sit furthest back.
                        timingLayer = new Container { RelativeSizeAxes = Axes.Both },
                        // Objects sit behind the ticks so the beat grid stays readable over them.
                        objectLayer = new Container { RelativeSizeAxes = Axes.Both },
                        gridLayer = new Container { RelativeSizeAxes = Axes.Both },
                        selectionLayer = new Container { RelativeSizeAxes = Axes.Both },
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
            };

            buildObjects();

            // Reflect selection changes from any source (here or the playfield).
            selection.Changed += refreshSelectionVisuals;
        }

        protected override void Update()
        {
            base.Update();

            // Scroll the content so the current time sits under the centre playhead.
            content.X = DrawWidth / 2f - (float)(currentTime() * pixelsPerMs);
            updateGrid();
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
        /// Lays out one marker container ("blueprint") per hit object at its time position. Each is kept
        /// in <see cref="blueprints"/> so a move drag can translate just the selected ones, lazer-style.
        /// </summary>
        private void buildObjects()
        {
            content.Width = contentWidth();
            objectLayer.Clear();
            objectBounds.Clear();
            blueprints.Clear();
            blueprintBaseX.Clear();

            buildTimingPoints();

            for (int i = 0; i < beatmap.HitObjects.Count; i++)
            {
                var o = beatmap.HitObjects[i];
                float x = (float)(o.StartTime * pixelsPerMs);
                Color4 combo = OsuColour.ComboColourFor(o.ComboIndex);

                // Children are positioned relative to the blueprint (local 0 = the object's start).
                // An explicit width keeps the marker visible through the masked timeline as it scrolls.
                var blueprint = new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    X = x,
                    Width = (float)(o.Duration * pixelsPerMs) + HEIGHT,
                };

                switch (o.Kind)
                {
                    case HitObjectKind.Slider:
                        float w = (float)(o.Duration * pixelsPerMs);
                        // The body spans the head's left edge to the tail's right edge so it lines up with
                        // both circles, which then sit on top at the ends.
                        blueprint.Add(bar(-object_size / 2f, w + object_size, combo));
                        blueprint.Add(dot(w, combo, null));            // tail (on the end tick)
                        blueprint.Add(dot(0, combo, o.ComboNumber));   // head, drawn on top
                        break;

                    case HitObjectKind.Spinner:
                        blueprint.Add(bar(0, (float)(o.Duration * pixelsPerMs), OsuColour.TextMuted));
                        break;

                    default:
                        blueprint.Add(dot(0, combo, o.ComboNumber));
                        break;
                }

                objectLayer.Add(blueprint);
                blueprints[o.Id] = blueprint;
                blueprintBaseX[o.Id] = x;
                objectBounds.Add(new ObjBounds(o.Id, o.StartTime, o.StartTime + o.Duration));
            }

            refreshSelectionVisuals();
        }

        /// <summary>
        /// Draws a vertical line at each timing point, scrolling with the content: red for uninherited
        /// (BPM) points, green for inherited (slider-velocity) ones - matching osu!lazer's timeline.
        /// </summary>
        private void buildTimingPoints()
        {
            timingLayer.Clear();

            foreach (var tp in beatmap.TimingPoints)
            {
                Color4 colour = tp.Uninherited
                    ? new Color4(0.92f, 0.26f, 0.30f, 0.85f)   // red: BPM
                    : new Color4(0.30f, 0.82f, 0.40f, 0.85f);  // green: SV

                float x = (float)(tp.Time * pixelsPerMs);

                timingLayer.Add(new Box
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2f,
                    X = x,
                    Colour = colour,
                });

                // A pill readout just right of the line, below the baseline: BPM for red, "x" multiplier for green.
                string label = tp.Uninherited
                    ? tp.Value.ToString("0.##", CultureInfo.InvariantCulture)
                    : tp.Value.ToString("0.##", CultureInfo.InvariantCulture) + "x";

                timingLayer.Add(new CircularContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    X = x + 3,
                    Y = baseline_y + 9,
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 7f,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(colour.R, colour.G, colour.B, 1f) },
                        new SpriteText
                        {
                            Padding = new MarginPadding { Horizontal = 5f, Vertical = 1f },
                            Text = label,
                            Colour = OsuColour.BackgroundDark,
                            Font = FontUsage.Default.With(size: 13, weight: "Bold"),
                        },
                    },
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
        private Drawable dot(float x, Color4 colour, int? comboNumber)
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

            int used = 0;
            var ticks = beatTicksBetween(from, to);

            foreach (var (time, colour, height) in ticks)
            {
                Box line = used < gridPool.Count ? gridPool[used] : addGridLine();
                used++;

                line.Alpha = 1;
                line.X = (float)(time * pixelsPerMs);
                line.Colour = colour;
                line.Height = height;
            }

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

        /// <summary>Enumerates beat-grid ticks (time, colour, height fraction) within a time window.</summary>
        private IEnumerable<(double time, Color4 colour, float height)> beatTicksBetween(double from, double to)
        {
            var result = new List<(double, Color4, float)>();
            if (beatmap.BeatPoints.Count == 0)
                return result;

            const int safety_cap = 4000;
            int produced = 0;

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

                while (time <= segEnd && time <= to && produced < safety_cap)
                {
                    if (time >= from)
                    {
                        (Color4 colour, float height) = tickStyle(k, point.Meter);
                        result.Add((time, colour, height));
                        produced++;
                    }

                    k++;
                    time = point.Time + k * step;
                }
            }

            return result;
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
            // Right-click quick-delete (M2): remove the object under the cursor, or the whole selection
            // if that object is part of it - matching osu!lazer's right-click delete.
            if (e.Button == MouseButton.Right)
            {
                int id = objectAt(timeAt(e.ScreenSpaceMousePosition));
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
            int hit = objectAt(timeAt(e.ScreenSpaceMousePosition));

            if (hit < 0)
            {
                // Clicking empty space clears the selection (unless adding to it).
                if (!e.ControlPressed)
                    selection.Clear();
            }
            else if (e.ControlPressed)
            {
                selection.Toggle(hit);
            }
            else
            {
                selection.SetSingle(hit);
            }

            return true;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            double startTime = timeAt(e.ScreenSpaceMouseDownPosition);
            int id = objectAt(startTime);

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

            updateDragSelection(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (moving)
                actions.MoveSelectionTime(timeAt(e.ScreenSpaceMousePosition) - moveStartTime, moveGrabbedId);
            else
                updateDragSelection(e.ScreenSpaceMousePosition);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (moving)
            {
                moving = false;
                actions.EndMove();
                return;
            }

            dragBox?.Expire();
            dragBox = null;
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

        /// <summary>The id of the object whose marker covers the given time, or -1 if none.</summary>
        private int objectAt(double time)
        {
            double toleranceMs = (HEIGHT * 0.52f / 2f) / pixelsPerMs;

            int best = -1;
            double bestDistance = double.MaxValue;

            foreach (var b in objectBounds)
            {
                if (time < b.StartTime - toleranceMs || time > b.EndTime + toleranceMs)
                    continue;

                double distance = Math.Min(Math.Abs(time - b.StartTime), Math.Abs(time - b.EndTime));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = b.Id;
                }
            }

            return best;
        }

        /// <summary>
        /// Draws a single group outline around all selected objects (their combined time extent), matching
        /// osu!lazer's timeline, where one box surrounds the whole selection rather than each object.
        /// </summary>
        private void refreshSelectionVisuals()
        {
            selectionLayer.Clear();
            selectionLayer.X = 0; // reset any live move-preview offset

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

            float pad = object_size * 0.3f;
            float startX = (float)(minStart * pixelsPerMs);
            float endX = (float)(maxEnd * pixelsPerMs);

            // Wrap the object row (above the baseline), centred on the objects.
            selectionLayer.Add(new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                X = startX - pad,
                Y = object_centre_y,
                Width = (endX - startX) + pad * 2,
                Height = object_size + pad,
                Masking = true,
                CornerRadius = object_size * 0.3f,
                BorderThickness = 2.5f,
                BorderColour = OsuColour.Yellow,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.08f), AlwaysPresent = true },
            });
        }
    }
}
