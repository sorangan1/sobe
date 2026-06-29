using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Annotations;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Renders the Review layer's annotations inside the play area (osu!pixel space, scaled with the playfield).
    /// Floating text notes that fade in as the playhead approaches their time; while a note is shown, the hit
    /// objects its timestamp refers to are highlighted in the author's colour (via <see cref="OnHighlightsChanged"/>),
    /// fading in sync with the note. Notes wrap to a max width and scroll past a max height. Interactive (drag /
    /// click-to-edit / clickable timestamps) only while <see cref="Editable"/> (Review mode); the layer is only
    /// shown while <see cref="Active"/> (Review mode, or the "show always" toggle).
    /// </summary>
    public partial class AnnotationLayer : CompositeDrawable
    {
        private const float note_width = 240;
        private const float note_text_max_height = 150;

        /// <summary>The editor's current (visual) time in ms, used to fade notes by their time window.</summary>
        public Func<double>? TimeSource;

        /// <summary>When true, notes accept input (drag/click/timestamps). When false, the layer is purely visual.</summary>
        public bool Editable;

        /// <summary>When true the layer is shown (Review mode or the "show always" toggle); otherwise hidden + inert.</summary>
        public bool Active;

        /// <summary>Raised when a note is clicked while editable (opens the note editor).</summary>
        public Action<Annotation>? NoteActivated;

        /// <summary>Raised when a note drag begins (so the editor can snapshot for undo before the move).</summary>
        public Action? NoteMoveStart;

        /// <summary>Raised after a note is dragged to a new position (commit the move).</summary>
        public Action<Annotation>? NoteMoved;

        /// <summary>Raised when a line is clicked while editable (deletes it).</summary>
        public Action<Annotation>? LineClicked;

        /// <summary>Raised when a note's own timestamp chip is clicked: seek to it and select its referenced objects.</summary>
        public Action<Annotation>? OnTimestampActivated;

        /// <summary>Raised when an inline timestamp inside a note's text is clicked: seek + select by combo numbers.</summary>
        public Action<double, List<int>>? OnTextTimestampActivated;

        /// <summary>Raised when hovering an inline timestamp in note text: show a pattern preview (time, combos, screen pos).</summary>
        public Action<double, List<int>, Vector2>? OnTextTimestampHover;

        /// <summary>Raised when the cursor leaves an inline timestamp (hide the preview).</summary>
        public Action? OnTextTimestampHoverLost;

        /// <summary>Formats a note's osu!-style timestamp (mm:ss:fff + object combo numbers) for display.</summary>
        public Func<double, List<int>?, string>? TimestampFormatter;

        /// <summary>Pushed every frame: per object id, the colour + alpha to draw its highlight at (alpha tracks the note's fade).</summary>
        public Action<Dictionary<int, (Colour4 colour, float alpha)>>? OnHighlightsChanged;

        private readonly Dictionary<string, AnnotationNote> notes = new Dictionary<string, AnnotationNote>();
        private readonly List<AnnotationStroke> strokes = new List<AnnotationStroke>();
        private readonly Dictionary<int, (Colour4 colour, float alpha)> highlightBuffer = new Dictionary<int, (Colour4, float)>();

        // mm:ss:fff with an optional (1,2,3) object list - the osu! timestamp format.
        private static readonly Regex timestamp_regex = new Regex(@"\d{1,2}:\d{2}:\d{3}(?:\s*\([\d,|]+\))?", RegexOptions.Compiled);

        public AnnotationLayer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // The layer itself never blocks input; only its note children do (and only when editable).
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => false;

        /// <summary>Rebuilds the note/line drawables from the document's annotations.</summary>
        public void SetAnnotations(IReadOnlyList<Annotation> annotations)
        {
            ClearInternal();
            notes.Clear();
            strokes.Clear();

            foreach (var a in annotations)
            {
                if (a.Kind == Annotation.KindNote)
                {
                    string timestamp = TimestampFormatter?.Invoke(a.Time, a.Objects) ?? defaultTimestamp(a);
                    var note = new AnnotationNote(a, timestamp)
                    {
                        LayerEditable = () => Editable,
                        OnActivate = () => NoteActivated?.Invoke(a),
                        OnMoveBegin = () => NoteMoveStart?.Invoke(),
                        OnMoved = () => NoteMoved?.Invoke(a),
                        OnTimestamp = () => OnTimestampActivated?.Invoke(a),
                        OnTextTimestamp = (time, combos) => OnTextTimestampActivated?.Invoke(time, combos),
                        OnTextTimestampHover = (time, combos, pos) => OnTextTimestampHover?.Invoke(time, combos, pos),
                        OnTextTimestampHoverLost = () => OnTextTimestampHoverLost?.Invoke(),
                    };
                    notes[a.Id] = note;
                    AddInternal(note);
                }
                else if ((a.Kind == Annotation.KindShape || a.Kind == Annotation.KindStroke) && a.Points is { Count: >= 2 })
                {
                    var stroke = new AnnotationStroke(a)
                    {
                        LayerEditable = () => Editable,
                        OnDelete = () => LineClicked?.Invoke(a),
                        OnMoveBegin = () => NoteMoveStart?.Invoke(),
                        OnMoved = () => NoteMoved?.Invoke(a),
                    };
                    strokes.Add(stroke);
                    AddInternal(stroke);
                }
            }
        }

        private static string defaultTimestamp(Annotation a)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, a.Time));
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";
        }

        protected override void Update()
        {
            base.Update();

            double now = TimeSource?.Invoke() ?? 0;
            foreach (var note in notes.Values)
                note.UpdateForTime(now);
            foreach (var stroke in strokes)
                stroke.UpdateForTime(now);

            updateHighlights();
        }

        /// <summary>
        /// Each frame, builds the per-object highlight (colour + alpha) from the notes that reference it and pushes
        /// it out. The alpha tracks the note's own fade, so the object highlight fades in/out in sync with its note.
        /// When several notes share an object, the most-visible one wins.
        /// </summary>
        private void updateHighlights()
        {
            highlightBuffer.Clear();

            if (Active)
            {
                foreach (var note in notes.Values)
                {
                    if (note.Annotation.Objects == null)
                        continue;

                    float alpha = Editable && note.IsHovered ? 1f : note.TargetAlpha;
                    if (alpha <= 0.01f)
                        continue;

                    Colour4 colour = ParseColour(note.Annotation.Color);
                    foreach (int id in note.Annotation.Objects)
                    {
                        if (!highlightBuffer.TryGetValue(id, out var existing) || alpha > existing.alpha)
                            highlightBuffer[id] = (colour, alpha);
                    }
                }
            }

            OnHighlightsChanged?.Invoke(highlightBuffer);
        }

        private static Color4 ParseColour(string hex)
        {
            try { return Colour4.FromHex(hex); }
            catch { return EditorTheme.Colours.Accent; }
        }

        /// <summary>Parses an osu! timestamp string ("mm:ss:fff (1,2,3)") into its time (ms) and combo numbers.</summary>
        private static (double time, List<int> combos) parseTimestamp(string s)
        {
            var combos = new List<int>();
            int paren = s.IndexOf('(');
            string timePart = (paren >= 0 ? s.Substring(0, paren) : s).Trim();

            double time = 0;
            string[] tp = timePart.Split(':');
            if (tp.Length == 3
                && int.TryParse(tp[0], out int min) && int.TryParse(tp[1], out int sec) && int.TryParse(tp[2], out int ms))
                time = (min * 60 + sec) * 1000 + ms;

            if (paren >= 0)
            {
                int close = s.IndexOf(')', paren);
                int len = (close > paren ? close : s.Length) - paren - 1;
                if (len > 0)
                    foreach (string part in s.Substring(paren + 1, len).Split(',', '|'))
                        if (int.TryParse(part.Trim(), out int n))
                            combos.Add(n);
            }

            return (time, combos);
        }

        /// <summary>A single floating note: a coloured pin with a text bubble (type icon, author, timestamp, text).</summary>
        private partial class AnnotationNote : Container
        {
            public Func<bool>? LayerEditable;
            public Action? OnActivate;
            public Action? OnMoveBegin;
            public Action? OnMoved;
            public Action? OnTimestamp;
            public Action<double, List<int>>? OnTextTimestamp;
            public Action<double, List<int>, Vector2>? OnTextTimestampHover;
            public Action? OnTextTimestampHoverLost;

            public Annotation Annotation => annotation;
            public float TargetAlpha => targetAlpha;

            private readonly Annotation annotation;
            private readonly string timestamp;
            private Container bubble = null!;
            private BasicScrollContainer textScroll = null!;
            private FillFlowContainer textFlow = null!;
            private float targetAlpha = 1f;
            private bool lastHovered;
            private Vector2 grabOffset;

            public AnnotationNote(Annotation annotation, string timestamp)
            {
                this.annotation = annotation;
                this.timestamp = timestamp;
                Origin = Anchor.BottomCentre; // the pin tip sits at the anchor point
                AutoSizeAxes = Axes.Both;
                Position = new Vector2(annotation.X, annotation.Y);
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                Color4 colour = ParseColour(annotation.Color);

                InternalChildren = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            bubble = new Container
                            {
                                AutoSizeAxes = Axes.Y,
                                Width = note_width,
                                Masking = true,
                                CornerRadius = 5,
                                BorderThickness = 1.5f,
                                BorderColour = colour,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.07f, 0.07f, 0.09f, 0.94f) },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Padding = new MarginPadding { Horizontal = 7, Vertical = 5 },
                                        Spacing = new Vector2(0, 3),
                                        Children = new Drawable[]
                                        {
                                            // Header row: the type icon + author name + a clickable timestamp chip.
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(4, 0),
                                                Children = new Drawable[]
                                                {
                                                    new SpriteIcon
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                        Icon = ReviewIcons.For(annotation.Type),
                                                        Size = new Vector2(11),
                                                        Colour = colour,
                                                    },
                                                    new SpriteText
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                        Text = annotation.Author,
                                                        Colour = colour,
                                                        Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                                                    },
                                                    new TimestampChip(timestamp, colour, () => OnTimestamp?.Invoke())
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                    },
                                                },
                                            },
                                            textScroll = new BasicScrollContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Height = 0,
                                                ScrollbarVisible = false,
                                                Child = textFlow = new FillFlowContainer
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    AutoSizeAxes = Axes.Y,
                                                    Direction = FillDirection.Full,
                                                    Spacing = new Vector2(4, 3),
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                            new Circle
                            {
                                Size = new Vector2(11),
                                Colour = colour,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                            },
                        },
                    },
                };

                buildText(annotation.Text);
            }

            /// <summary>Builds the note body: plain words plus inline, clickable osu! timestamps.</summary>
            private void buildText(string text)
            {
                textFlow.Clear();
                if (string.IsNullOrEmpty(text))
                    return;

                int last = 0;
                foreach (Match m in timestamp_regex.Matches(text))
                {
                    if (m.Index > last)
                        addWords(text.Substring(last, m.Index - last));

                    var (time, combos) = parseTimestamp(m.Value);
                    textFlow.Add(new TimestampChip(m.Value, EditorTheme.Colours.Accent, () => OnTextTimestamp?.Invoke(time, combos))
                    {
                        OnHoverShow = pos => OnTextTimestampHover?.Invoke(time, combos, pos),
                        OnHoverHide = () => OnTextTimestampHoverLost?.Invoke(),
                    });

                    last = m.Index + m.Length;
                }

                if (last < text.Length)
                    addWords(text.Substring(last));
            }

            private void addWords(string segment)
            {
                foreach (string word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    textFlow.Add(new SpriteText
                    {
                        Text = word,
                        Colour = EditorTheme.Colours.Text,
                        Font = FontUsage.Default.With(size: 13),
                    });
            }

            /// <summary>Refreshes alpha from the playhead distance, and clamps the text area to its max height (then scrolls).</summary>
            public void UpdateForTime(double now)
            {
                double dt = Math.Abs(now - annotation.Time);
                double half = Math.Max(1, annotation.WindowMs * 0.25);
                // Full within half the window, fading out across the second half; fully gone outside it (the note
                // is still reachable via its icon on the bottom timeline).
                targetAlpha = (float)Math.Clamp(1 - Math.Max(0, dt - half) / half, 0, 1);
                Alpha = targetAlpha;

                // Grow with the text up to a cap, then let the body scroll.
                textScroll.Height = Math.Min(textFlow.DrawHeight, note_text_max_height);

                // Hover visual driven by IsHovered (true even while the cursor is over a child like the timestamp
                // chip), so moving onto the timestamp doesn't drop the note's hover state.
                bool hov = (LayerEditable?.Invoke() ?? false) && IsHovered;
                if (hov != lastHovered)
                {
                    lastHovered = hov;
                    bubble.ScaleTo(hov ? 1.03f : 1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }
            }

            // Catch the mouse on the bubble only while the layer is in edit mode (and the note is visible).
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
                => (LayerEditable?.Invoke() ?? false) && targetAlpha > 0.05f && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnClick(ClickEvent e)
            {
                OnActivate?.Invoke();
                return true;
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (!(LayerEditable?.Invoke() ?? false))
                    return false;

                // Remember where on the note we grabbed so it follows the cursor instead of snapping its pin to it.
                grabOffset = Position - Parent!.ToLocalSpace(e.ScreenSpaceMouseDownPosition);
                OnMoveBegin?.Invoke(); // snapshot for undo before the move mutates the model
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                Vector2 osu = Parent!.ToLocalSpace(e.ScreenSpaceMousePosition) + grabOffset;
                osu = new Vector2(
                    Math.Clamp(osu.X, 0, Beatmaps.ParsedBeatmap.PLAYFIELD_WIDTH),
                    Math.Clamp(osu.Y, 0, Beatmaps.ParsedBeatmap.PLAYFIELD_HEIGHT));
                annotation.X = osu.X;
                annotation.Y = osu.Y;
                Position = osu;
            }

            protected override void OnDragEnd(DragEndEvent e) => OnMoved?.Invoke();
        }

        /// <summary>A clickable osu! timestamp chip (underlined, accent-ish). Click seeks; hover gives a subtle pop.</summary>
        private partial class TimestampChip : CompositeDrawable
        {
            /// <summary>Hover handlers (set only for inline timestamps in the text) to show/hide a pattern preview.</summary>
            public Action<Vector2>? OnHoverShow;
            public Action? OnHoverHide;

            private readonly string text;
            private readonly Color4 colour;
            private readonly Action onClick;
            private Box underline = null!;

            public TimestampChip(string text, Color4 colour, Action onClick)
            {
                this.text = text;
                this.colour = colour;
                this.onClick = onClick;
                AutoSizeAxes = Axes.Both;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = text,
                            Colour = colour,
                            Font = FontUsage.Default.With(size: 11, weight: "SemiBold"),
                        },
                        underline = new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = colour,
                            Alpha = 0.5f,
                        },
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                underline.FadeTo(1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                this.ScaleTo(1.08f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                OnHoverShow?.Invoke(ScreenSpaceDrawQuad.TopLeft); // inline timestamps: pop a pattern preview
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                underline.FadeTo(0.5f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                this.ScaleTo(1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                OnHoverHide?.Invoke();
            }
        }

        /// <summary>
        /// A freehand "Draw" stroke (osu!pixel space) rendered as a smoothed path. A static one (Kind=shape) fades
        /// in/out with the playhead like a note; a timed one (Kind=stroke) is revealed progressively over its
        /// <see cref="Annotation.DurationMs"/> from its start time (it "draws itself" as you play). Clicking the
        /// stroke (while editable) deletes it - hit-testing uses the path itself, so only the line is clickable.
        /// </summary>
        private partial class AnnotationStroke : CompositeDrawable
        {
            public Func<bool>? LayerEditable;
            public Action? OnDelete;
            public Action? OnMoveBegin;
            public Action? OnMoved;

            private readonly Annotation annotation;
            private SmoothPath path = null!;
            private Vector2[] points = Array.Empty<Vector2>();
            private float targetAlpha = 1f;
            private int lastCount = -1;
            private bool forceRebuild;
            private Vector2[] dragOriginal = Array.Empty<Vector2>();
            private Vector2 dragGrab;

            public AnnotationStroke(Annotation annotation)
            {
                this.annotation = annotation;
                RelativeSizeAxes = Axes.Both;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                Color4 colour = ParseColour(annotation.Color);
                var pts = annotation.Points!;
                points = new Vector2[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                    points[i] = new Vector2(pts[i][0], pts[i][1]);

                float thickness = annotation.Thickness ?? 3f;
                InternalChild = path = new SmoothPath { PathRadius = Math.Max(1.5f, thickness / 2f), Colour = colour };

                setVertices(points.Length);
            }

            /// <summary>Shows the first <paramref name="count"/> points of the stroke (absolute osu! coords, re-anchored).</summary>
            private void setVertices(int count)
            {
                count = Math.Clamp(count, 0, points.Length);
                if (count == lastCount && !forceRebuild)
                    return;
                lastCount = count;
                forceRebuild = false;

                if (count < 2)
                {
                    path.Vertices = Array.Empty<Vector2>();
                    return;
                }

                path.Vertices = points[..count];
                path.Position = -path.PositionInBoundingBox(Vector2.Zero); // keep vertices at their absolute coords
            }

            public void UpdateForTime(double now)
            {
                // Visible across its [Time, EndTime] range (set/tuned on the top timeline), with a short fade at each edge.
                const double fade = 150;
                double start = annotation.Time;
                double end = Math.Max(start + 1, annotation.EndTime ?? start + 1000);

                float a;
                if (now < start)
                    a = (float)Math.Clamp(1 - (start - now) / fade, 0, 1);
                else if (now <= end)
                    a = 1f;
                else
                    a = (float)Math.Clamp(1 - (now - end) / fade, 0, 1);

                targetAlpha = a;
                Alpha = a;
            }

            // Hit-test against the path itself (not the bounding box) so only the line is grabbable/clickable.
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
                => (LayerEditable?.Invoke() ?? false) && targetAlpha > 0.05f && path.ReceivePositionalInputAt(screenSpacePos);

            // Right-click deletes the stroke (left-click/drag is reserved for moving it, so moving is accident-free).
            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (e.Button == osuTK.Input.MouseButton.Right && (LayerEditable?.Invoke() ?? false))
                {
                    OnDelete?.Invoke();
                    return true;
                }
                return base.OnMouseDown(e);
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                if (e.Button != osuTK.Input.MouseButton.Left || !(LayerEditable?.Invoke() ?? false))
                    return false;

                dragGrab = ToLocalSpace(e.ScreenSpaceMouseDownPosition);
                dragOriginal = (Vector2[])points.Clone();
                OnMoveBegin?.Invoke(); // snapshot for undo before the move
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                Vector2 delta = ToLocalSpace(e.ScreenSpaceMousePosition) - dragGrab;
                for (int i = 0; i < points.Length; i++)
                    points[i] = dragOriginal[i] + delta;

                forceRebuild = true;
                setVertices(points.Length);
            }

            protected override void OnDragEnd(DragEndEvent e)
            {
                // Persist the moved points back to the model so the move survives save / undo snapshots.
                var pts = new List<float[]>(points.Length);
                foreach (var p in points)
                    pts.Add(new[] { p.X, p.Y });
                annotation.Points = pts;
                OnMoved?.Invoke();
            }
        }
    }
}
