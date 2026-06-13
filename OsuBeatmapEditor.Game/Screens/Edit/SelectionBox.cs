using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The transform box shown around the selection while Shift is held: a yellow outline with corner
    /// handles to scale, a handle above the top edge to rotate, and two buttons to flip H/V - mirroring
    /// osu!lazer's editor selection box. Lives in the playfield's osu!pixel coordinate space; gestures are
    /// reported through callbacks that the editor turns into rotate/scale/flip operations.
    /// </summary>
    public partial class SelectionBox : Container
    {
        /// <summary>Converts a screen-space position to osu!pixels (the playfield's local space).</summary>
        public Func<Vector2, Vector2> ScreenToOsu = p => p;

        public Action? TransformBegin;
        public Action? TransformEnd;
        public Action<float>? Rotate;                 // degrees from gesture start
        public Action<Vector2, Anchor>? Resize;       // osu!px width/height delta, anchored at a corner
        public Action<bool>? Flip;                    // true = horizontal

        /// <summary>The current box centre in osu!pixels (its Position is the top-left).</summary>
        public Vector2 CentreOsu => Position + Size / 2f;

        private static readonly Color4 accent = OsuColour.Yellow;

        public SelectionBox()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;

            // Outline drawn as four thin edges so the interior stays click-through to the playfield.
            AddRange(new Drawable[]
            {
                edge(Anchor.TopLeft, Axes.X),
                edge(Anchor.BottomLeft, Axes.X),
                edge(Anchor.TopLeft, Axes.Y),
                edge(Anchor.TopRight, Axes.Y),

                // Rotation grips sit just outside each corner (drawn first so the scale handles are on top).
                new RotationHandle(this, Anchor.TopLeft),
                new RotationHandle(this, Anchor.TopRight),
                new RotationHandle(this, Anchor.BottomLeft),
                new RotationHandle(this, Anchor.BottomRight),

                new ScaleHandle(this, Anchor.TopLeft),
                new ScaleHandle(this, Anchor.TopRight),
                new ScaleHandle(this, Anchor.BottomLeft),
                new ScaleHandle(this, Anchor.BottomRight),

                // Flip buttons sit centred just below the bottom edge, clear of the corner scale/rotation handles.
                new FillFlowContainer
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.TopCentre,
                    Position = new Vector2(0, 8),
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new FlipButton(this, horizontal: true),
                        new FlipButton(this, horizontal: false),
                    },
                },
            });
        }

        private static Drawable edge(Anchor anchor, Axes along) => new Box
        {
            Anchor = anchor,
            Origin = anchor,
            RelativeSizeAxes = along,
            Width = along == Axes.X ? 1f : 0f,
            Height = along == Axes.Y ? 1f : 0f,
            Size = along == Axes.X ? new Vector2(1f, 1.5f) : new Vector2(1.5f, 1f),
            Colour = new Color4(accent.R, accent.G, accent.B, 0.9f),
        };

        // --- handles ---

        private partial class ScaleHandle : Box
        {
            private readonly SelectionBox box;
            private readonly Anchor corner;
            private Vector2 startOsu;

            public ScaleHandle(SelectionBox box, Anchor corner)
            {
                this.box = box;
                this.corner = corner;
                Anchor = corner;
                Origin = Anchor.Centre;
                Size = new Vector2(10);
                Colour = accent;
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                startOsu = box.ScreenToOsu(e.ScreenSpaceMouseDownPosition);
                box.TransformBegin?.Invoke();
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                Vector2 delta = box.ScreenToOsu(e.ScreenSpaceMousePosition) - startOsu;

                // A right/bottom corner grows the box in the drag direction; a left/top corner grows it inverted.
                float sx = (corner & Anchor.x2) > 0 ? delta.X : (corner & Anchor.x0) > 0 ? -delta.X : 0;
                float sy = (corner & Anchor.y2) > 0 ? delta.Y : (corner & Anchor.y0) > 0 ? -delta.Y : 0;

                box.Resize?.Invoke(new Vector2(sx, sy), corner);
            }

            protected override void OnDragEnd(DragEndEvent e) => box.TransformEnd?.Invoke();
        }

        private partial class RotationHandle : CircularContainer
        {
            private readonly SelectionBox box;
            private Vector2 centreOsu;
            private float startAngle;

            public RotationHandle(SelectionBox box, Anchor corner)
            {
                this.box = box;
                Anchor = corner;
                Origin = Anchor.Centre;
                // Sit just outside the corner, diagonally away from the box centre.
                Position = new Vector2((corner & Anchor.x0) > 0 ? -13 : 13, (corner & Anchor.y0) > 0 ? -13 : 13);
                Size = new Vector2(14);
                Masking = true;
                BorderThickness = 2;
                BorderColour = accent;
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(accent.R, accent.G, accent.B, 0.2f) };
            }

            protected override bool OnDragStart(DragStartEvent e)
            {
                centreOsu = box.CentreOsu;
                startAngle = angleTo(box.ScreenToOsu(e.ScreenSpaceMouseDownPosition));
                box.TransformBegin?.Invoke();
                return true;
            }

            protected override void OnDrag(DragEvent e)
            {
                float now = angleTo(box.ScreenToOsu(e.ScreenSpaceMousePosition));
                box.Rotate?.Invoke(now - startAngle);
            }

            protected override void OnDragEnd(DragEndEvent e) => box.TransformEnd?.Invoke();

            private float angleTo(Vector2 osu) => MathHelper.RadiansToDegrees((float)Math.Atan2(osu.Y - centreOsu.Y, osu.X - centreOsu.X));
        }

        private partial class FlipButton : CircularContainer
        {
            private readonly SelectionBox box;
            private readonly bool horizontal;

            public FlipButton(SelectionBox box, bool horizontal)
            {
                this.box = box;
                this.horizontal = horizontal;
                Size = new Vector2(16);
                Masking = true;
                CornerRadius = 3;
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(accent.R, accent.G, accent.B, 0.85f) },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = horizontal ? "H" : "V",
                        Colour = OsuColour.BackgroundDark,
                        Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                box.Flip?.Invoke(horizontal);
                return true;
            }
        }
    }
}
