using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Draggable control-point editor for a selected slider, modelled on osu!lazer's
    /// <c>PathControlPointVisualiser</c>: a control polygon with a handle per control point (white = segment
    /// continuation, red = segment start / sharp corner, i.e. a non-null <see cref="SliderControlPoint.Type"/>).
    /// Dragging a handle reshapes (and resizes) the slider live; double-clicking a handle toggles its corner;
    /// right-clicking un-corners or deletes it; Ctrl+left-clicking the body inserts a control point. Edits commit
    /// through <see cref="IEditorActions"/>.
    /// </summary>
    public partial class SliderControlPointVisualiser : CompositeDrawable
    {
        /// <summary>Raised when an edge node is clicked: its node index (0 = head, <c>Slides</c> = tail).</summary>
        public Action<int>? PartNodeClicked;

        /// <summary>Raised when the slider body (not a handle or edge node) is single-clicked.</summary>
        public Action? BodyClicked;

        private readonly int sliderId;
        private readonly int tailNodeIndex;
        private readonly float diameter;
        private readonly IEditorActions actions;
        private readonly double pixelLength;

        private readonly List<SliderControlPoint> controlPoints;
        private readonly List<ControlPointPiece> pieces = new List<ControlPointPiece>();

        private Container lineLayer = null!;
        private Container pieceLayer = null!;
        private SmoothPath preview = null!;

        public SliderControlPointVisualiser(HitObjectModel slider, float diameter, IEditorActions actions)
        {
            sliderId = slider.Id;
            tailNodeIndex = Math.Max(1, slider.Slides);
            this.diameter = diameter;
            this.actions = actions;
            controlPoints = slider.ControlPoints!.ToList();
            pixelLength = SliderGeometry.PathLength(slider.Path ?? Array.Empty<Vector2>());

            RelativeSizeAxes = Axes.Both;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChildren = new Drawable[]
            {
                preview = new SmoothPath
                {
                    PathRadius = Math.Max(1.5f, diameter / 2f - 3f),
                    Colour = new Color4(OsuColour.Yellow.R, OsuColour.Yellow.G, OsuColour.Yellow.B, 0.35f),
                    Alpha = 0,
                },
                lineLayer = new Container { RelativeSizeAxes = Axes.Both },
                pieceLayer = new Container { RelativeSizeAxes = Axes.Both },
            };

            rebuildPieces();
            rebuildPolygon();
        }

        /// <summary>(Re)creates a handle per control point.</summary>
        private void rebuildPieces()
        {
            pieceLayer.Clear();
            pieces.Clear();

            for (int i = 0; i < controlPoints.Count; i++)
            {
                var piece = new ControlPointPiece(i, controlPoints[i].IsSegmentStart, diameter * 0.26f)
                {
                    Position = controlPoints[i].Position,
                    Moved = handleMoved,
                    Toggled = handleToggled,
                    RightClicked = handleRightClicked,
                    Clicked = pieceClicked,
                };
                pieces.Add(piece);
                pieceLayer.Add(piece);
            }
        }

        /// <summary>Redraws the thin control-polygon lines connecting consecutive control points.</summary>
        private void rebuildPolygon()
        {
            lineLayer.Clear();
            for (int i = 1; i < controlPoints.Count; i++)
                lineLayer.Add(polygonSegment(controlPoints[i - 1].Position, controlPoints[i].Position));
        }

        private Drawable polygonSegment(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            return new Box
            {
                Position = a,
                Origin = Anchor.CentreLeft,
                Width = d.Length,
                Height = 1.5f,
                Rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X)),
                Colour = new Color4(1f, 1f, 1f, 0.35f),
            };
        }

        private void handleMoved(int index, Vector2 screenPosition, bool finished)
        {
            Vector2 osu = clamp(ToLocalSpace(screenPosition));
            controlPoints[index] = controlPoints[index] with { X = osu.X, Y = osu.Y };

            pieces[index].Position = osu;
            rebuildPolygon();
            showPreview();

            if (finished)
            {
                actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
                actions.ClearSliderPreview();
            }
            else
            {
                // Show, in real time, how long the reshaped slider will occupy on the top timeline.
                actions.PreviewSliderResize(sliderId, controlPoints.ToArray());
            }
        }

        /// <summary>Double-click a handle: toggle whether it starts a new segment (a sharp corner).</summary>
        private void handleToggled(int index)
        {
            // The head always starts the first segment; it can't be un-typed.
            if (index <= 0)
                return;

            controlPoints[index] = controlPoints[index].IsSegmentStart
                ? controlPoints[index] with { Type = null }
                : controlPoints[index] with { Type = segmentTypeAt(index) };

            actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
        }

        /// <summary>Right-click a handle: un-corner a segment-start point, otherwise delete it (keeping at least two).</summary>
        private void handleRightClicked(int index)
        {
            if (index > 0 && controlPoints[index].IsSegmentStart)
            {
                controlPoints[index] = controlPoints[index] with { Type = null };
            }
            else
            {
                if (controlPoints.Count <= 2)
                    return;

                controlPoints.RemoveAt(index);

                // The new head must keep a definite type.
                if (controlPoints[0].Type == null)
                    controlPoints[0] = controlPoints[0] with { Type = SliderPathType.Bezier };
            }

            actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
        }

        /// <summary>The spline type of the segment that control point <paramref name="index"/> belongs to.</summary>
        private SliderPathType segmentTypeAt(int index)
        {
            for (int i = index; i >= 0; i--)
            {
                if (controlPoints[i].Type is SliderPathType t)
                    return t;
            }

            return SliderPathType.Bezier;
        }

        /// <summary>A single click on a handle selects its edge node (head/tail) for the two-stage part selection.</summary>
        private void pieceClicked(int index)
        {
            if (index == 0)
                PartNodeClicked?.Invoke(0);
            else if (index == controlPoints.Count - 1)
                PartNodeClicked?.Invoke(tailNodeIndex);
            // Intermediate anchors aren't edge nodes; a click just keeps the slider selected.
        }

        /// <summary>
        /// Click handling on the body (clicks on a handle go to the piece itself): Ctrl+left-click anywhere on
        /// the body inserts a control point on the nearest polygon segment (it takes priority, so it still works
        /// right on top of the head/tail circle); a plain click on the head/tail circle selects that edge node;
        /// a plain click on the body selects the body part.
        /// </summary>
        protected override bool OnClick(ClickEvent e)
        {
            Vector2 pos = clamp(ToLocalSpace(e.ScreenSpaceMousePosition));

            // Ctrl+left-click inserts a new anchor, checked first so it works even when hovering the head/tail
            // circle (where edge-node selection would otherwise consume the click).
            if (e.ControlPressed && nearPath(pos, Math.Max(8f, diameter / 2f)))
            {
                int insertAt = nearestSegmentIndex(pos);
                controlPoints.Insert(insertAt, new SliderControlPoint(pos));
                actions.UpdateSliderAnchors(sliderId, controlPoints.ToArray());
                return true;
            }

            // The head/tail circle is larger than its handle, so cover the circle area for edge-node selection.
            // The tail sits at the rendered path end (not the last control point, which a length trim can move).
            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);
            float nodeRadius = diameter / 2f;
            if (Vector2.Distance(pos, controlPoints[0].Position) <= nodeRadius)
            {
                PartNodeClicked?.Invoke(0);
                return true;
            }
            if (path.Count > 0 && Vector2.Distance(pos, path[^1]) <= nodeRadius)
            {
                PartNodeClicked?.Invoke(tailNodeIndex);
                return true;
            }

            if (!nearPath(pos, Math.Max(8f, diameter / 2f)))
                return false; // clicks away from the body fall through to the playfield

            BodyClicked?.Invoke(); // selecting the body part keeps this slider selected
            return true;
        }

        private int nearestSegmentIndex(Vector2 p)
        {
            int best = controlPoints.Count;
            float bestDist = float.MaxValue;

            for (int i = 1; i < controlPoints.Count; i++)
            {
                float d = pointSegmentDistance(p, controlPoints[i - 1].Position, controlPoints[i].Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        private bool nearPath(Vector2 p, float radius)
        {
            var path = SliderGeometry.ComputePath(controlPoints, pixelLength);
            for (int i = 1; i < path.Count; i++)
            {
                if (pointSegmentDistance(p, path[i - 1], path[i]) <= radius)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Live preview of the reshaped slider body using the TICK-SNAPPED length (exactly what the commit will
        /// produce), so the mapper sees the real-time size snapping as they drag - not the raw control polygon.
        /// </summary>
        private void showPreview()
        {
            var path = actions.SnappedSliderPath(sliderId, controlPoints.ToArray());
            if (path.Count < 2)
            {
                preview.Alpha = 0;
                return;
            }

            preview.Alpha = 1;
            preview.Vertices = path;
            preview.Position = -preview.PositionInBoundingBox(Vector2.Zero);
        }

        private static float pointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.LengthSquared;
            float t = lengthSq <= 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector2 clamp(Vector2 p) => new Vector2(
            Math.Clamp(p.X, 0, ParsedBeatmap.PLAYFIELD_WIDTH),
            Math.Clamp(p.Y, 0, ParsedBeatmap.PLAYFIELD_HEIGHT));

        /// <summary>A single draggable control-point handle; reports moves, double-click toggles and right-clicks upward.</summary>
        private partial class ControlPointPiece : CircularContainer
        {
            public Action<int, Vector2, bool>? Moved;
            public Action<int>? Toggled;
            public Action<int>? RightClicked;
            public Action<int>? Clicked;

            private readonly int index;
            private readonly Box fill;
            private double lastClickTime = double.MinValue;

            public ControlPointPiece(int index, bool red, float size)
            {
                this.index = index;

                Origin = Anchor.Centre;
                Size = new Vector2(size);
                Masking = true;
                BorderThickness = 2f;
                BorderColour = Color4.White;
                Child = fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = red ? OsuColour.Pink : Color4.White,
                    Alpha = red ? 0.95f : 0.85f,
                };
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                // Consume right-clicks (delete / un-corner) so the playfield doesn't delete the whole slider.
                if (e.Button == osuTK.Input.MouseButton.Right)
                {
                    RightClicked?.Invoke(index);
                    return true;
                }

                return base.OnMouseDown(e);
            }

            protected override bool OnDragStart(DragStartEvent e) => true;

            protected override void OnDrag(DragEvent e) => Moved?.Invoke(index, e.ScreenSpaceMousePosition, false);

            protected override void OnDragEnd(DragEndEvent e) => Moved?.Invoke(index, e.ScreenSpaceMousePosition, true);

            protected override bool OnClick(ClickEvent e)
            {
                double now = Time.Current;
                if (now - lastClickTime < 250)
                    Toggled?.Invoke(index);
                else
                    Clicked?.Invoke(index);
                lastClickTime = now;
                return true; // swallow so the playfield doesn't treat it as an empty-space click
            }

            protected override bool OnHover(HoverEvent e)
            {
                fill.Alpha = 1f;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                fill.Alpha = 0.85f;
                base.OnHoverLost(e);
            }
        }
    }
}
