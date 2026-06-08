using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <c>PathControlPointVisualiser</c>: a control polygon with a handle per anchor (white = smooth,
    /// red = sharp corner). Dragging a handle reshapes the slider live; double-clicking an interior handle
    /// toggles its corner. Edits are committed to the model through <see cref="IEditorActions"/>.
    /// </summary>
    public partial class SliderControlPointVisualiser : CompositeDrawable
    {
        private readonly int sliderId;
        private readonly float diameter;
        private readonly IEditorActions actions;
        private readonly double pixelLength;
        private readonly char curveType;

        private readonly List<SliderAnchor> anchors;
        private readonly List<ControlPointPiece> pieces = new List<ControlPointPiece>();

        private Container lineLayer = null!;
        private Container pieceLayer = null!;
        private SmoothPath preview = null!;

        public SliderControlPointVisualiser(HitObjectModel slider, float diameter, IEditorActions actions)
        {
            sliderId = slider.Id;
            this.diameter = diameter;
            this.actions = actions;
            anchors = slider.Anchors!.ToList();
            curveType = slider.CurveType;
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

        /// <summary>(Re)creates a handle per anchor.</summary>
        private void rebuildPieces()
        {
            pieceLayer.Clear();
            pieces.Clear();

            for (int i = 0; i < anchors.Count; i++)
            {
                var piece = new ControlPointPiece(i, anchors[i].Red, diameter * 0.34f)
                {
                    Position = anchors[i].Position,
                    Moved = handleMoved,
                    Toggled = handleToggled,
                    RightClicked = handleRightClicked,
                };
                pieces.Add(piece);
                pieceLayer.Add(piece);
            }
        }

        /// <summary>Redraws the thin control-polygon lines connecting consecutive anchors.</summary>
        private void rebuildPolygon()
        {
            lineLayer.Clear();
            for (int i = 1; i < anchors.Count; i++)
                lineLayer.Add(polygonSegment(anchors[i - 1].Position, anchors[i].Position));
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
            anchors[index] = anchors[index] with { X = osu.X, Y = osu.Y };

            pieces[index].Position = osu;
            rebuildPolygon();
            showPreview();

            if (finished)
                actions.UpdateSliderAnchors(sliderId, anchors.ToArray());
        }

        private void handleToggled(int index)
        {
            // The head and tail can't be sharp corners (a corner is a boundary between two segments).
            if (index <= 0 || index >= anchors.Count - 1)
                return;

            anchors[index] = anchors[index] with { Red = !anchors[index].Red };
            actions.UpdateSliderAnchors(sliderId, anchors.ToArray());
        }

        /// <summary>Right-click: un-sharpen a red corner, otherwise delete the anchor (keeping at least two).</summary>
        private void handleRightClicked(int index)
        {
            if (anchors[index].Red)
            {
                anchors[index] = anchors[index] with { Red = false };
            }
            else
            {
                if (anchors.Count <= 2)
                    return;
                anchors.RemoveAt(index);
            }

            actions.UpdateSliderAnchors(sliderId, anchors.ToArray());
        }

        /// <summary>
        /// Double-clicking on the slider body (not on a handle) inserts a new anchor on the nearest control-
        /// polygon segment, so the curve gains a control point there - matching osu!lazer.
        /// </summary>
        protected override bool OnClick(ClickEvent e)
        {
            Vector2 pos = clamp(ToLocalSpace(e.ScreenSpaceMousePosition));

            // Clicks away from the slider body fall through to the playfield (select others / deselect).
            if (!nearPath(pos, Math.Max(8f, diameter / 2f)))
                return false;

            double now = Time.Current;
            bool doubleClick = now - lastBodyClickTime < 250;
            lastBodyClickTime = now;

            if (doubleClick)
            {
                int insertAt = nearestSegmentIndex(pos);
                anchors.Insert(insertAt, new SliderAnchor(pos));
                actions.UpdateSliderAnchors(sliderId, anchors.ToArray());
            }

            return true; // a single click on the body keeps this slider selected
        }

        /// <summary>The insertion index (1..count) on the control polygon segment closest to <paramref name="p"/>.</summary>
        private int nearestSegmentIndex(Vector2 p)
        {
            int best = anchors.Count; // default: append before the tail
            float bestDist = float.MaxValue;

            for (int i = 1; i < anchors.Count; i++)
            {
                float d = pointSegmentDistance(p, anchors[i - 1].Position, anchors[i].Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Whether the position lies within <paramref name="radius"/> of the rendered slider polyline.</summary>
        private bool nearPath(Vector2 p, float radius)
        {
            var path = SliderGeometry.ComputePath(anchors, SliderGeometry.AdjustType(curveType, anchors), pixelLength);
            for (int i = 1; i < path.Count; i++)
            {
                if (pointSegmentDistance(p, path[i - 1], path[i]) <= radius)
                    return true;
            }
            return false;
        }

        private static float pointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.LengthSquared;
            float t = lengthSq <= 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        private double lastBodyClickTime = double.MinValue;

        /// <summary>Shows a live preview of the reshaped slider body while a handle is being dragged.</summary>
        private void showPreview()
        {
            // Full control-polygon path (length follows the anchors on commit, as in lazer).
            char type = SliderGeometry.AdjustType(curveType, anchors);
            var path = SliderGeometry.ComputePath(anchors, type);
            if (path.Count < 2)
            {
                preview.Alpha = 0;
                return;
            }

            preview.Alpha = 1;
            preview.Vertices = path;
            preview.Position = -preview.PositionInBoundingBox(Vector2.Zero);
        }

        private static Vector2 clamp(Vector2 p) => new Vector2(
            Math.Clamp(p.X, 0, ParsedBeatmap.PLAYFIELD_WIDTH),
            Math.Clamp(p.Y, 0, ParsedBeatmap.PLAYFIELD_HEIGHT));

        /// <summary>A single draggable anchor handle; reports moves and double-click corner toggles upward.</summary>
        private partial class ControlPointPiece : CircularContainer
        {
            public Action<int, Vector2, bool>? Moved;
            public Action<int>? Toggled;
            public Action<int>? RightClicked;

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
                // Consume right-clicks on the handle (delete / un-sharpen) so the playfield doesn't delete the slider.
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
