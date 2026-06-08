using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Renders a single hit object on the editor playfield: the circle/slider body with its combo
    /// number, an approach circle that shrinks onto the object as its time nears, reverse arrows for
    /// repeating sliders, and a ball (with follow circle) that travels the path across its duration.
    /// Visibility is driven via <see cref="UpdateAt"/> each frame.
    /// </summary>
    public partial class DrawableHitObject : CompositeDrawable
    {
        private const double fade_out = 240;         // fade-out duration after it
        private const double time_fade_in = 400;     // osu! Standard object fade-in window (ms), capped to preempt
        private const float approach_start_scale = 4f;
        private const float follow_scale = 1.9f;

        private readonly HitObjectModel hitObject;
        private readonly float diameter;

        private Container? approachCircle;
        private Drawable? sliderBall;
        private Drawable? spinnerRotor;

        private readonly IReadOnlyList<Vector2>? path;
        private float[] cumulativeLength = Array.Empty<float>();
        private float totalLength;

        /// <summary>This object's stable id (for shared selection across edits/reordering).</summary>
        public int Id => hitObject.Id;

        /// <summary>The visual stack offset (osu!pixels) applied to this object's whole rendering.</summary>
        public Vector2 StackOffset => StackOffsetFor(hitObject.StackHeight, diameter);

        /// <summary>The object's head/centre position (stack-aware), for hit-testing and box selection.</summary>
        public Vector2 HeadPosition => (path is { Count: > 0 } p ? p[0] : new Vector2(hitObject.X, hitObject.Y)) + StackOffset;

        /// <summary>The diagonal offset a stacked object is nudged by, per the osu! stacking convention.</summary>
        public static Vector2 StackOffsetFor(int stackHeight, float diameter) => new Vector2(stackHeight * diameter * -0.05f);

        public DrawableHitObject(HitObjectModel hitObject, float diameter)
        {
            this.hitObject = hitObject;
            this.diameter = diameter;
            path = hitObject.Path;

            RelativeSizeAxes = Axes.Both;
            Position = StackOffset; // nudge the whole object by its stack offset
            Alpha = 0;
        }

        /// <summary>Roughly how visible this object currently is (used to gate playfield selection).</summary>
        public bool IsHittable => Alpha > 0.05f;

        /// <summary>True if the given osu!pixel position lies on this object's body/circle (stack-aware).</summary>
        public bool BodyContains(Vector2 osuPosition)
        {
            float r = diameter / 2f;
            Vector2 p = osuPosition - StackOffset;

            if (hitObject.Kind == HitObjectKind.Spinner)
                return Vector2.Distance(p, new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f)) <= 130f;

            if (hitObject.Kind == HitObjectKind.Slider && path is { Count: > 1 })
            {
                for (int i = 1; i < path.Count; i++)
                {
                    if (pointSegmentDistance(p, path[i - 1], path[i]) <= r)
                        return true;
                }

                return false;
            }

            return Vector2.Distance(p, new Vector2(hitObject.X, hitObject.Y)) <= r;
        }

        private static float pointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.LengthSquared;
            float t = lengthSq <= 0 ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Color4 combo = OsuColour.ComboColourFor(hitObject.ComboIndex);

            if (hitObject.Kind == HitObjectKind.Slider && path is { Count: > 1 })
            {
                buildArcLengths();

                // White outer border, then a darker translucent body inset within it, so the body reads
                // as a tube with a clean rim that ends exactly at the head/tail circles.
                var border = new SmoothPath
                {
                    PathRadius = diameter / 2f,
                    Colour = Color4.White,
                };
                border.Vertices = path;
                border.Position = -border.PositionInBoundingBox(Vector2.Zero);

                var body = new SmoothPath
                {
                    PathRadius = Math.Max(1f, diameter / 2f - Math.Max(2f, diameter * 0.1f)),
                    Colour = new Color4(combo.R * 0.45f, combo.G * 0.45f, combo.B * 0.45f, 0.85f),
                };
                body.Vertices = path;
                // Align the path's local coordinate space with playfield (osu!pixel) coordinates.
                body.Position = -body.PositionInBoundingBox(Vector2.Zero);

                AddInternal(border);
                AddInternal(body);
                AddInternal(circle(path[^1], combo));              // tail
                AddInternal(circle(path[0], combo));               // head
                AddInternal(numberText(path[0]));

                // Reverse arrows: one at the tail if it repeats, plus the head for further repeats.
                if (hitObject.Slides >= 2)
                    AddInternal(reverseArrow(path[^1], angleDeg(path[^1], path[^2])));
                if (hitObject.Slides >= 3)
                    AddInternal(reverseArrow(path[0], angleDeg(path[0], path[1])));

                AddInternal(sliderBall = ball(path[0], combo));
                AddInternal(approachCircle = approach(path[0], combo));
            }
            else if (hitObject.Kind == HitObjectKind.Spinner)
            {
                buildSpinner();
            }
            else
            {
                var pos = new Vector2(hitObject.X, hitObject.Y);
                AddInternal(circle(pos, combo));
                AddInternal(numberText(pos));
                AddInternal(approachCircle = approach(pos, combo));
            }
        }

        private void buildArcLengths()
        {
            cumulativeLength = new float[path!.Count];
            float acc = 0;
            for (int i = 1; i < path.Count; i++)
            {
                acc += (path[i] - path[i - 1]).Length;
                cumulativeLength[i] = acc;
            }
            totalLength = acc;
        }

        private Drawable circle(Vector2 position, Color4 fill) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter),
            Masking = true,
            BorderThickness = diameter * 0.08f,
            BorderColour = Color4.White,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = fill,
                Alpha = 0.9f,
            },
        };

        private Drawable numberText(Vector2 position) => new SpriteText
        {
            Position = position,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Text = hitObject.ComboNumber.ToString(),
            Colour = Color4.White,
            Font = FontUsage.Default.With(size: diameter * 0.5f, weight: "Bold"),
        };

        private Drawable reverseArrow(Vector2 position, float rotation) => new SpriteText
        {
            Position = position,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.Centre,
            Rotation = rotation,
            Text = ">",
            Colour = Color4.White,
            Font = FontUsage.Default.With(size: diameter * 0.8f, weight: "Bold"),
        };

        private Container approach(Vector2 position, Color4 colour) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter),
            Masking = true,
            BorderThickness = diameter * 0.06f,
            BorderColour = colour,
            Alpha = 0,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        /// <summary>
        /// Builds a clear, centred spinner: a large dimmed disc, a rotating cross marker so its motion
        /// reads at a glance, and a "SPIN!" label - rendered at the osu! Standard playfield centre.
        /// </summary>
        private void buildSpinner()
        {
            var centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f);
            const float radius = 130f;

            AddInternal(new CircularContainer
            {
                Position = centre,
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Masking = true,
                BorderThickness = 6,
                BorderColour = OsuColour.TextMuted,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.5f },
                    // Mid ring for depth.
                    new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(radius),
                        Masking = true,
                        BorderThickness = 3,
                        BorderColour = new Color4(OsuColour.Purple.R, OsuColour.Purple.G, OsuColour.Purple.B, 0.8f),
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                    },
                },
            });

            // Rotating cross so the spin direction/progress is visible while scrubbing.
            spinnerRotor = new Container
            {
                Position = centre,
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Children = new Drawable[]
                {
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(radius * 2, 4), Colour = OsuColour.Pink },
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(4, radius * 2), Colour = OsuColour.Pink },
                },
            };
            AddInternal(spinnerRotor);

            AddInternal(new SpriteText
            {
                Position = centre,
                Origin = Anchor.Centre,
                Text = "SPIN!",
                Colour = Color4.White,
                Font = FontUsage.Default.With(size: 28, weight: "Bold"),
            });
        }

        private Drawable ball(Vector2 position, Color4 colour) => new Container
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter * follow_scale),
            Alpha = 0,
            Children = new Drawable[]
            {
                // Follow circle.
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = diameter * 0.08f,
                    BorderColour = new Color4(colour.R, colour.G, colour.B, 0.8f),
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0, AlwaysPresent = true },
                },
                // Inner ball.
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                    Masking = true,
                    BorderThickness = diameter * 0.1f,
                    BorderColour = Color4.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0.95f },
                },
            },
        };

        private static float angleDeg(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            return MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X));
        }

        /// <summary>Updates this object's visibility/animation for the given playback time and AR preempt (ms).</summary>
        public void UpdateAt(double time, double preempt)
        {
            double start = hitObject.StartTime;
            bool hasDuration = hitObject.Kind is HitObjectKind.Slider or HitObjectKind.Spinner;
            double visibleEnd = start + (hasDuration ? hitObject.Duration : 0);

            // osu! Standard appearance: the object fades in over a fixed window (TimeFadeIn, capped to
            // preempt for very high AR) once it first becomes visible at start - preempt, then holds.
            double fadeIn = Math.Min(time_fade_in, preempt);

            float alpha;
            if (time < start)
                alpha = (float)((time - (start - preempt)) / fadeIn);
            else
                alpha = time <= visibleEnd ? 1f : 1f - (float)((time - visibleEnd) / fade_out);

            Alpha = Math.Clamp(alpha, 0f, 1f);

            if (approachCircle != null)
            {
                if (time < start)
                {
                    // Scale shrinks from 4x to 1x linearly across the whole preempt window, while the
                    // ring itself fades in over min(TimeFadeIn * 2, preempt) - matching osu!lazer.
                    float t = (float)Math.Clamp((start - time) / preempt, 0, 1);
                    approachCircle.Scale = new Vector2(1 + (approach_start_scale - 1) * t);

                    double approachFadeIn = Math.Min(time_fade_in * 2, preempt);
                    approachCircle.Alpha = (float)Math.Clamp((time - (start - preempt)) / approachFadeIn, 0, 1);
                }
                else
                {
                    approachCircle.Alpha = 0;
                }
            }

            if (spinnerRotor != null && hitObject.Duration > 0)
            {
                // Two full turns across the spinner's duration, clamped to its active window.
                double progress = Math.Clamp((time - start) / hitObject.Duration, 0, 1);
                spinnerRotor.Rotation = (float)(progress * 720);
            }

            if (sliderBall != null)
            {
                if (time >= start && time <= visibleEnd && hitObject.Duration > 0)
                {
                    sliderBall.Alpha = 1;
                    sliderBall.Position = ballPosition(time, start);
                }
                else
                {
                    sliderBall.Alpha = 0;
                }
            }
        }

        private Vector2 ballPosition(double time, double start)
        {
            double spanDuration = hitObject.Duration / hitObject.Slides;
            double p = (time - start) / spanDuration;          // progress measured in spans
            int span = Math.Clamp((int)p, 0, hitObject.Slides - 1);
            float frac = (float)(p - span);
            float param = (span & 1) == 0 ? frac : 1 - frac;   // bounce back on odd spans
            return pointAtParam(Math.Clamp(param, 0, 1));
        }

        private Vector2 pointAtParam(float param)
        {
            if (path == null || path.Count == 0)
                return Vector2.Zero;

            if (totalLength <= 0)
                return path[0];

            float target = param * totalLength;
            for (int i = 1; i < path.Count; i++)
            {
                if (cumulativeLength[i] >= target)
                {
                    float segLen = cumulativeLength[i] - cumulativeLength[i - 1];
                    float f = segLen <= 0 ? 0 : (target - cumulativeLength[i - 1]) / segLen;
                    return Vector2.Lerp(path[i - 1], path[i], f);
                }
            }

            return path[^1];
        }
    }
}
