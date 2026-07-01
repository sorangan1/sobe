using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Renders a single hit object on the editor playfield: the circle/slider body with its combo
    /// number, an approach circle that shrinks onto the object as its time nears, reverse arrows for
    /// repeating sliders, and a ball (with follow circle) that travels the path across its duration.
    /// Appearance is scheduled as transforms against the audio clock (lazer-style), so the framework animates
    /// and re-evaluates it while the editor seeks; the slider ball position is updated per-frame while active.
    /// </summary>
    public partial class DrawableHitObject : CompositeDrawable
    {
        private const double time_fade_in = 400;     // osu! Standard object fade-in window (ms)
        private const double preempt_min = 450;      // minimum preempt (AR10); lazer scales fade-in below it
        private const float approach_start_scale = 4f;
        private const float follow_scale = 1.9f;

        private readonly HitObjectModel hitObject;
        private readonly float diameter;
        private readonly double preempt;

        // Spacing (osu!pixels) between consecutive slider ticks along the path; 0 = no ticks (non-sliders).
        private readonly double tickDistance;

        // How long the object lingers/fades after its end. Passed in (the constructor needs it for the
        // lifetime, before [Resolved] settings are injected) so already-played notes stay visible, lazer-style.
        private readonly double fadeOut;

        // Mod-preview flags. hidden = Hidden (objects fade out before the hit, no approach circle); flipText =
        // HardRock flips the whole play area, so text drawn here must be counter-flipped to read upright.
        private bool hidden;
        private readonly bool flipText;

        // When set (authorship-colour mode), tints the object by who placed it instead of the combo colour.
        private readonly Color4? authorColour;

        private Container? approachCircle;
        private Container? hitRing;
        private Drawable? sliderBall;
        private Drawable? followCircle;

        // The slider tube. It is the ONLY per-object drawable that owns a GPU vertex buffer (it's a path); it is
        // realized/released with the object's lifetime so off-screen sliders don't keep their geometry resident.
        private SliderBodyPath? sliderBody;
        private Color4 comboColour;

        // Reverse arrows at the slider ends; shown only while the ball is heading toward an end where a reverse
        // waits (lazer's per-repeat arrow), so the head arrow never overlaps the combo number at slider start.
        private Drawable? reverseArrowHead;
        private Drawable? reverseArrowTail;
        private const double reverse_fade_ms = 150; // matches lazer's ApplyRepeatFadeIn(Arrow, 150)

        // Slider-tick feedback: arc distances of the ticks, the matching dot drawables (so each can be hidden
        // as the ball sweeps over it), the ball's last arc position (to detect crossings), and a 0..1 pulse
        // that briefly expands the follow circle as the ball passes a tick.
        private float[] tickArcLengths = Array.Empty<float>();
        private readonly List<Drawable> tickDots = new List<Drawable>();
        private float lastBallArc = -1f;
        private float tickPulse;

        private const double tick_pulse_ms = 160;  // how long the follow-circle pop lasts
        private const float tick_pulse_amp = 0.18f; // how far it expands (fraction)
        private const float tick_base_alpha = 0.6f; // ticks sit slightly translucent until collected
        private const double tick_fade_ms = 90;     // how quickly a tick fades out as the ball reaches it
        private const double tick_fade_in_ms = 150; // how quickly a tick fades in (lazer's DrawableSliderTick ANIM_DURATION)
        private Drawable? spinnerRotor;
        private Box? clickFlash;

        private readonly IReadOnlyList<Vector2>? path;
        private float[] cumulativeLength = Array.Empty<float>();
        private float totalLength;

        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        [Resolved]
        private EditableBeatmap editable { get; set; } = null!;

        // The active osu! skin (null = none). When present, hit-object pieces are drawn from its textures and
        // the procedural fallbacks below are skipped per-element. Resolved before the BDL builds the visuals.
        [Resolved(CanBeNull = true)]
        private SkinManager? skinManager { get; set; }

        private Skin? skin => skinManager?.Current.Value;

        /// <summary>Configurable opacity for object fills/bodies.</summary>
        private float objectOpacity => settings.ObjectBackgroundOpacity.Value;

        /// <summary>Configurable outline thickness, as a fraction of the circle diameter.</summary>
        private float borderFactor => settings.ObjectBorderThickness.Value;

        /// <summary>This object's stable id (for shared selection across edits/reordering).</summary>
        public int Id => hitObject.Id;

        /// <summary>The visual stack offset (osu!pixels) applied to this object's whole rendering.</summary>
        public Vector2 StackOffset => StackOffsetFor(hitObject.StackHeight, diameter);

        /// <summary>The object's head/centre position (stack-aware), for hit-testing and box selection.</summary>
        public Vector2 HeadPosition => (path is { Count: > 0 } p ? p[0] : new Vector2(hitObject.X, hitObject.Y)) + StackOffset;

        /// <summary>A slider's geometric tail position (stack-aware); the head position for non-sliders.</summary>
        public Vector2 TailPosition => (path is { Count: > 0 } p ? p[^1] : new Vector2(hitObject.X, hitObject.Y)) + StackOffset;

        /// <summary>True for sliders (the only objects with selectable head/tail nodes).</summary>
        public bool IsSlider => hitObject.Kind == HitObjectKind.Slider;

        /// <summary>This slider's tail node index (= number of spans); 0 for non-sliders.</summary>
        public int TailNodeIndex => IsSlider ? Math.Max(1, hitObject.Slides) : 0;

        /// <summary>
        /// The slider node (0 = head, <see cref="TailNodeIndex"/> = tail) whose circle the given osu!pixel
        /// position falls on, or -1 if neither / not a slider. The head wins ties.
        /// </summary>
        public int NodeAt(Vector2 osuPosition, float radius)
        {
            if (!IsSlider)
                return -1;

            if (Vector2.Distance(osuPosition, HeadPosition) <= radius)
                return 0;
            if (Vector2.Distance(osuPosition, TailPosition) <= radius)
                return TailNodeIndex;

            return -1;
        }

        /// <summary>The diagonal offset a stacked object is nudged by, per the osu! stacking convention.</summary>
        public static Vector2 StackOffsetFor(int stackHeight, float diameter) => new Vector2(stackHeight * diameter * -0.05f);

        public DrawableHitObject(HitObjectModel hitObject, float diameter, double preempt, double fadeOut, double tickDistance = 0, bool hidden = false, bool flipText = false, Color4? authorColour = null)
        {
            this.hitObject = hitObject;
            this.diameter = diameter;
            this.preempt = preempt;
            this.fadeOut = fadeOut;
            this.tickDistance = tickDistance;
            this.hidden = hidden;
            this.flipText = flipText;
            this.authorColour = authorColour;
            path = hitObject.Path;

            RelativeSizeAxes = Axes.Both;
            Position = StackOffset; // nudge the whole object by its stack offset
            Alpha = 0;

            // Lifetime window: visible from preempt before the start until the fade-out ends. The
            // LifetimeManagementContainer realises/updates/draws this object only while the clock is inside it.
            bool hasDuration = hitObject.Kind is HitObjectKind.Slider or HitObjectKind.Spinner;
            double visibleEnd = hitObject.StartTime + (hasDuration ? hitObject.Duration : 0);
            LifetimeStart = hitObject.StartTime - preempt;
            LifetimeEnd = visibleEnd + fadeOut;

            // Earlier objects draw in front (smaller depth = nearer), matching osu!'s approach-time stacking.
            Depth = (float)hitObject.StartTime;
        }

        /// <summary>Roughly how visible this object currently is (used to gate playfield selection).</summary>
        public bool IsHittable => IsAlive && Alpha > 0.05f;

        // Keep completed transforms so the framework re-evaluates them at any clock time, including when the
        // editor seeks backward (this is how osu!lazer makes hit objects rewindable - it overrides the getter).
        public override bool RemoveCompletedTransforms => false;

        // The LifetimeManagementContainer manages our lifetime; it must not remove us when we go out of it.
        public override bool RemoveWhenNotAlive => false;

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

        /// <summary>
        /// True if the position lies on a tappable circle of this object - a hit circle, or a slider's head/tail
        /// node - as opposed to merely its slider body. Used to prefer circles over slider bodies when picking,
        /// so an object tucked beneath a slider body can still be selected.
        /// </summary>
        public bool EndpointContains(Vector2 osuPosition)
        {
            float r = diameter / 2f;
            Vector2 p = osuPosition - StackOffset;

            if (hitObject.Kind == HitObjectKind.Spinner)
                return false;

            if (Vector2.Distance(p, new Vector2(hitObject.X, hitObject.Y)) <= r)
                return true;

            // Slider tail node.
            if (hitObject.Kind == HitObjectKind.Slider && path is { Count: > 0 })
                return Vector2.Distance(p, path[^1]) <= r;

            return false;
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
            Colour4 cc = editable.ComboColourFor(hitObject.ComboIndex);
            Color4 combo = authorColour ?? new Color4(cc.R, cc.G, cc.B, cc.A);

            if (hitObject.Kind == HitObjectKind.Slider && path is { Count: > 1 })
            {
                buildArcLengths();
                comboColour = combo;

                sliderBody = buildSliderBody();
                AddInternal(sliderBody);

                // Slider ticks: small dots along the path at the tick-rate spacing (under the head/ball,
                // over the body). Drawn once per span position - they repeat the same points each span.
                tickArcLengths = computeTickArcLengths();
                tickDots.Clear();
                foreach (float d in tickArcLengths)
                {
                    var t = tickDot(pointAtParam(d / totalLength), combo);
                    tickDots.Add(t);
                    AddInternal(t);
                }

                // No tail circle: the slider end is left as the body's rounded cap. The yellow selection
                // ring at the end is still drawn separately by the playfield's selection layer.
                AddInternal(circle(path[0], combo));               // head
                AddInternal(numberText(path[0]));

                // Reverse arrows: one at the tail if it repeats, plus the head for further repeats. They start
                // hidden and are faded in per-frame only while the ball is heading toward that end (see
                // updateReverseArrows), matching lazer where each repeat's arrow appears near its own time.
                if (hitObject.Slides >= 2)
                {
                    reverseArrowTail = reverseArrow(path[^1], angleDeg(path[^1], path[^2]));
                    reverseArrowTail.Alpha = 0;
                    AddInternal(reverseArrowTail);
                }
                if (hitObject.Slides >= 3)
                {
                    reverseArrowHead = reverseArrow(path[0], angleDeg(path[0], path[1]));
                    reverseArrowHead.Alpha = 0;
                    AddInternal(reverseArrowHead);
                }

                AddInternal(sliderBall = ball(path[0], combo));
                AddInternal(approachCircle = approach(path[0], combo));
                AddInternal(hitRing = hitExplosion(path[0], combo));
                AddInternal(flashLayer(path[0]));
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
                AddInternal(hitRing = hitExplosion(pos, combo));
                AddInternal(flashLayer(pos));
            }
        }

        /// <summary>
        /// Builds the slider tube (a direct port of lazer's DefaultDrawableSliderPath: a white rim on the outer
        /// <see cref="SliderBodyPath.BorderPortion"/> of the radius, then a gradient body filling inside it).
        /// This is the only drawable here that owns a GPU vertex buffer, so it is created/destroyed on demand.
        /// </summary>
        private SliderBodyPath buildSliderBody()
        {
            var body = new SliderBodyPath
            {
                PathRadius = diameter / 2f,
                BodyOpacity = objectOpacity,
                // Rim thickness matches the hit-circle ring in osu!pixels (both = diameter·borderFactor),
                // so the one outline setting drives the circle and the slider body together.
                BorderPortion = Math.Clamp(2f * borderFactor, 0.02f, 0.9f),
                // Always behind the head circle / ticks / ball (which sit at depth 0).
                Depth = 1f,
            };
            // Border/track colours + gradient opacity from the active skin (or the built-in dark-grey look).
            body.ApplySkinAppearance(skin, comboColour);
            body.Vertices = path!;
            // Align the path's local coordinate space with playfield (osu!pixel) coordinates.
            body.Position = -body.PositionInBoundingBox(Vector2.Zero);
            return body;
        }

        /// <summary>
        /// Realizes or releases the slider body in step with the object's lifetime. The body owns a GPU vertex
        /// buffer, so keeping every object's body resident for the whole map balloons GPU memory during playback.
        /// The lifetime container (which already culls update/draw) disposes it when the object scrolls off and
        /// rebuilds it when it scrolls back, bounding resident slider geometry to the visible window, like lazer.
        /// </summary>
        public void SetBodyRealized(bool realized)
        {
            if (hitObject.Kind != HitObjectKind.Slider || path is not { Count: > 1 } || !IsLoaded)
                return;

            if (realized)
            {
                if (sliderBody != null)
                    return;

                sliderBody = buildSliderBody();
                AddInternal(sliderBody);
            }
            else
            {
                if (sliderBody == null)
                    return;

                RemoveInternal(sliderBody, true); // dispose frees the GPU vertex buffer
                sliderBody = null;
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

        /// <summary>
        /// The on-path arc distances (osu!pixels from the head) of this slider's ticks, spaced
        /// <see cref="tickDistance"/> apart, dropping any within ~10 ms of the span end (osu!'s tick gap).
        /// </summary>
        private float[] computeTickArcLengths()
        {
            if (tickDistance <= 0 || totalLength <= 0)
                return Array.Empty<float>();

            double spanDuration = hitObject.Slides > 0 ? hitObject.Duration / hitObject.Slides : hitObject.Duration;
            double velocity = spanDuration > 0 ? totalLength / spanDuration : 0;
            float minFromEnd = (float)(velocity * 10); // osu! skips a tick within 10 ms of the span end
            float limit = totalLength - minFromEnd;

            const int safety_cap = 200;
            var list = new List<float>();
            for (double d = tickDistance; d < limit && list.Count < safety_cap; d += tickDistance)
                list.Add((float)d);
            return list.ToArray();
        }

        /// <summary>A small dot marking a slider tick, sitting on the body beneath the head circle and ball.</summary>
        private Drawable tickDot(Vector2 position, Color4 combo)
        {
            // Skinned slider tick (sliderscorepoint); falls back to the procedural ringed dot.
            var tex = skin?.GetTexture("sliderscorepoint");
            if (tex != null)
            {
                return new Sprite
                {
                    Position = position,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter * settings.SliderTickSize.Value),
                    Alpha = tick_base_alpha,
                    FillMode = FillMode.Fit,
                    Texture = tex,
                };
            }

            return new CircularContainer
            {
                Position = position,
                Origin = Anchor.Centre,
                Size = new Vector2(diameter * settings.SliderTickSize.Value),
                Alpha = tick_base_alpha,
                Masking = true,
                BorderThickness = Math.Max(1f, diameter * 0.02f),
                BorderColour = new Color4(combo.R * 0.6f, combo.G * 0.6f, combo.B * 0.6f, 1f),
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
            };
        }

        private Drawable circle(Vector2 position, Color4 fill)
        {
            // Skinned: hitcircle tinted by the combo colour, with the (untinted) overlay on top. Falls back to
            // the procedural ring when the skin doesn't ship a hitcircle.
            var hitTex = skin?.GetTexture("hitcircle");
            if (hitTex != null)
            {
                var container = new Container
                {
                    Position = position,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                };
                container.Add(new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Texture = hitTex,
                    Colour = fill,
                    Alpha = objectOpacity,
                });

                var overlayTex = skin!.GetTexture("hitcircleoverlay");
                if (overlayTex != null)
                {
                    container.Add(new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Texture = overlayTex,
                    });
                }

                return container;
            }

            return new CircularContainer
            {
                Position = position,
                Origin = Anchor.Centre,
                Size = new Vector2(diameter),
                Masking = true,
                BorderThickness = diameter * borderFactor,
                BorderColour = Color4.White,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = fill,
                    Alpha = objectOpacity,
                },
            };
        }

        /// <summary>A white disc filling the head circle, normally invisible; pulsed at the object's start time as a hit flash.</summary>
        private Drawable flashLayer(Vector2 position) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter),
            Masking = true,
            Child = clickFlash = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
                Alpha = 0,
                AlwaysPresent = true,
            },
        };

        private const double flash_duration = 620;

        private Drawable numberText(Vector2 position)
        {
            // Skinned digits ({prefix}-0..9) laid out horizontally with the skin's overlap, centred on the circle.
            var skinned = skin != null ? skinnedNumber(position) : null;
            if (skinned != null)
                return skinned;

            return new SpriteText
            {
                Position = position,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Text = hitObject.ComboNumber.ToString(),
                Colour = Color4.White,
                // Counter-flip when HardRock flips the whole play area, so the number stays upright.
                Scale = new Vector2(1, flipText ? -1 : 1),
                Font = FontUsage.Default.With(size: diameter * 0.5f, weight: "Bold"),
            };
        }

        /// <summary>
        /// Builds the combo number from the skin's digit textures (<c>{HitCirclePrefix}-N</c>), or null if the
        /// skin has no digit font. Digits are sized to ~45% of the circle and overlapped by the skin's
        /// HitCircleOverlap (scaled to that size), matching osu!'s legacy number layout.
        /// </summary>
        private Drawable? skinnedNumber(Vector2 position)
        {
            string prefix = skin!.Config.HitCirclePrefix;
            string digits = hitObject.ComboNumber.ToString();

            float height = diameter * 0.45f;

            var flow = new FillFlowContainer
            {
                Position = position,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                // Counter-flip when HardRock flips the whole play area, so the number stays upright.
                Scale = new Vector2(1, flipText ? -1 : 1),
            };

            foreach (char d in digits)
            {
                var tex = skin.GetTexture($"{prefix}-{d}");
                if (tex == null)
                    return null; // incomplete digit font: fall back to the text glyphs

                float aspect = tex.DisplayHeight > 0 ? tex.DisplayWidth / tex.DisplayHeight : 1f;
                // Overlap is authored against the texture's own pixel height; scale it to our render height.
                float overlap = skin.Config.HitCircleOverlap * (height / Math.Max(1f, tex.DisplayHeight));

                flow.Add(new Sprite
                {
                    Texture = tex,
                    Size = new Vector2(height * aspect, height),
                    Margin = new MarginPadding { Horizontal = -overlap / 2f },
                });
            }

            return flow;
        }

        // Reverse arrow at a slider end. Skinned: the reversearrow texture (points right at 0°, so it rotates by
        // the raw travel angle). Procedural fallback: an equilateral triangle that points up, hence the +90.
        private Drawable reverseArrow(Vector2 position, float rotation)
        {
            var tex = skin?.GetTexture("reversearrow");
            if (tex != null)
            {
                return new Sprite
                {
                    Position = position,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Rotation = rotation,
                    Size = new Vector2(diameter * 0.7f),
                    FillMode = FillMode.Fit,
                    Texture = tex,
                };
            }

            return new Triangle
            {
                Position = position,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                Rotation = rotation + 90,
                Size = new Vector2(diameter * 0.3f),
                Colour = Color4.White,
            };
        }

        private Container approach(Vector2 position, Color4 colour)
        {
            // Skinned approachcircle, tinted by the combo colour; sized to the circle so its 4x->1x shrink lands
            // exactly on the hit circle. Falls back to the procedural ring.
            var tex = skin?.GetTexture("approachcircle");
            if (tex != null)
            {
                return new Container
                {
                    Position = position,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                    Alpha = 0,
                    Child = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Texture = tex,
                        Colour = colour,
                    },
                };
            }

            return new CircularContainer
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
        }

        /// <summary>
        /// An outlined ring that sits exactly on the circle, then expands outward and fades as the object is
        /// hit - osu!lazer's circle "explosion" feel (scale ~1 → 1.5, fade out over ~260 ms).
        /// </summary>
        private Container hitExplosion(Vector2 position, Color4 colour) => new CircularContainer
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter),
            Masking = true,
            BorderThickness = diameter * 0.045f,
            BorderColour = colour,
            Alpha = 0,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0, AlwaysPresent = true },
        };

        private const double hit_explosion_scale_duration = 200; // quick expand, then it holds
        private const double hit_explosion_duration = 520;       // total time it lingers while fading

        /// <summary>
        /// Builds a minimalist spinner: a thin outer ring over a faint fill, a single slim pointer that
        /// sweeps counter-clockwise to show progress, and a small centre dot - at the playfield centre.
        /// </summary>
        private void buildSpinner()
        {
            var centre = new Vector2(ParsedBeatmap.PLAYFIELD_WIDTH / 2f, ParsedBeatmap.PLAYFIELD_HEIGHT / 2f);
            const float radius = 130f;

            // Faint disc with a thin, clean rim.
            AddInternal(new CircularContainer
            {
                Position = centre,
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Masking = true,
                BorderThickness = 2,
                BorderColour = new Color4(1f, 1f, 1f, 0.6f),
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.25f },
            });

            // A single slim pointer from the centre to the rim; rotating it shows the spin.
            spinnerRotor = new Container
            {
                Position = centre,
                Origin = Anchor.Centre,
                Size = new Vector2(radius * 2),
                Child = new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.BottomCentre,
                    Size = new Vector2(2.5f, radius),
                    Colour = new Color4(1f, 1f, 1f, 0.85f),
                },
            };
            AddInternal(spinnerRotor);

            // Small centre dot.
            AddInternal(new CircularContainer
            {
                Position = centre,
                Origin = Anchor.Centre,
                Size = new Vector2(10),
                Masking = true,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
            });
        }

        private Drawable ball(Vector2 position, Color4 colour) => new Container
        {
            Position = position,
            Origin = Anchor.Centre,
            Size = new Vector2(diameter * follow_scale),
            Alpha = 0,
            Children = new[]
            {
                // Follow circle. Stored so it can "pop" outward when the ball passes a tick.
                followCircle = buildFollowCircle(colour),
                // Inner ball.
                buildInnerBall(colour),
            },
        };

        /// <summary>The follow circle: the skin's sliderfollowcircle if present, else the procedural ring.</summary>
        private Drawable buildFollowCircle(Color4 colour)
        {
            var tex = skin?.GetTexture("sliderfollowcircle");
            if (tex != null)
            {
                return new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Child = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Texture = tex,
                    },
                };
            }

            return new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                BorderThickness = diameter * borderFactor,
                BorderColour = new Color4(colour.R, colour.G, colour.B, 0.8f),
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White, Alpha = 0, AlwaysPresent = true },
            };
        }

        /// <summary>The slider ball: the skin's sliderb (tinted only when AllowSliderBallTint), else procedural.</summary>
        private Drawable buildInnerBall(Color4 colour)
        {
            var tex = skin?.GetTexture("sliderb") ?? skin?.GetTexture("sliderb0");
            if (tex != null)
            {
                Color4 ballColour = skin!.Config.AllowSliderBallTint
                    ? colour
                    : (skin.Config.SliderBall is { } sb ? new Color4(sb.R, sb.G, sb.B, sb.A) : Color4.White);

                return new Sprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(diameter),
                    FillMode = FillMode.Fit,
                    Texture = tex,
                    Colour = ballColour,
                };
            }

            return new CircularContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(diameter),
                Masking = true,
                BorderThickness = diameter * borderFactor,
                BorderColour = Color4.White,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour, Alpha = 0.95f },
            };
        }

        private static float angleDeg(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            return MathHelper.RadiansToDegrees((float)Math.Atan2(d.Y, d.X));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            applyAnimation();
        }

        /// <summary>
        /// Toggles the Hidden mod preview in place: only the fade (and approach circle) change, so we just clear
        /// and re-schedule this object's transforms instead of recreating the drawable - the Hidden chip stays
        /// snappy even on dense maps. No-op when the value is unchanged.
        /// </summary>
        public void SetHidden(bool value)
        {
            if (hidden == value)
                return;

            hidden = value;

            // Not loaded yet: LoadComplete's applyAnimation will run with the new value, so nothing to redo.
            if (!IsLoaded)
                return;

            ClearTransforms(true); // includes children (approach circle, slider ball, hit ring, flash)
            applyAnimation();
        }

        /// <summary>
        /// Schedules this object's appearance as transforms against the (audio) clock - the declarative model
        /// osu!lazer uses, so the framework animates it and re-evaluates correctly while the editor seeks.
        /// Visuals are unchanged from the previous per-frame version.
        /// </summary>
        private void applyAnimation()
        {
            double start = hitObject.StartTime;
            bool hasDuration = hitObject.Kind is HitObjectKind.Slider or HitObjectKind.Spinner;
            double visibleEnd = start + (hasDuration ? hitObject.Duration : 0);

            // Fade in over TimeFadeIn from start-preempt, hold, then fade out (lazer scales the window for AR>10).
            double fadeIn = time_fade_in * Math.Min(1, preempt / preempt_min);

            if (hidden)
            {
                // Hidden (osu!'s OsuModHidden): fade in over the first 40% of the preempt, then fade out over the
                // next 30% so a circle is gone before it's hit; a slider/spinner fades across its whole body.
                double hdFadeIn = preempt * 0.4;
                double hdFadeOutStart = start - preempt + hdFadeIn;
                double hdFadeOut = hasDuration ? Math.Max(1, visibleEnd - hdFadeOutStart) : preempt * 0.3;

                using (BeginAbsoluteSequence(start - preempt))
                    this.FadeInFromZero(hdFadeIn);
                using (BeginAbsoluteSequence(hdFadeOutStart))
                    this.FadeOut(hdFadeOut);
            }
            else
            {
                using (BeginAbsoluteSequence(start - preempt))
                    this.FadeInFromZero(fadeIn);
                using (BeginAbsoluteSequence(visibleEnd))
                    this.FadeOut(fadeOut);
            }

            // Hidden suppresses the approach circle entirely. Reset it unconditionally so an in-place toggle
            // back to Hidden leaves it hidden (no stale transforms keeping it visible).
            if (approachCircle != null)
            {
                approachCircle.Alpha = 0;
                approachCircle.Scale = new Vector2(approach_start_scale);

                if (!hidden)
                {
                    double approachFadeIn = Math.Min(fadeIn * 2, preempt);
                    using (BeginAbsoluteSequence(start - preempt))
                    {
                        approachCircle.FadeTo(1f, approachFadeIn);
                        approachCircle.ScaleTo(1f, preempt); // linear shrink 4x -> 1x across the preempt window
                    }

                    using (BeginAbsoluteSequence(start))
                        approachCircle.FadeOut();
                }
            }

            if (spinnerRotor != null && hitObject.Duration > 0)
            {
                using (BeginAbsoluteSequence(start))
                    spinnerRotor.RotateTo(-720f, hitObject.Duration); // two turns counter-clockwise
            }

            if (sliderBall != null && hitObject.Duration > 0)
            {
                sliderBall.Alpha = 0;
                using (BeginAbsoluteSequence(start))
                    sliderBall.FadeIn();
                using (BeginAbsoluteSequence(visibleEnd))
                    sliderBall.FadeOut();
            }

            // Hit explosion: a combo-coloured ring that expands outward and fades as the object is hit.
            if (hitRing != null)
            {
                hitRing.Alpha = 0;
                hitRing.Scale = Vector2.One;
                using (BeginAbsoluteSequence(start))
                {
                    // Expand a little, quickly (eased), then hold at that size while fading out over a longer time.
                    hitRing.ScaleTo(1.22f, hit_explosion_scale_duration, Easing.OutQuint);
                    hitRing.FadeTo(0.9f).FadeOut(hit_explosion_duration, Easing.OutQuad);
                }
            }

            // Hit flash: a quick white fill over the head as playback reaches the object's start time.
            if (clickFlash != null)
            {
                clickFlash.Alpha = 0;
                using (BeginAbsoluteSequence(start))
                    clickFlash.FadeTo(0.55f).FadeOut(flash_duration, Easing.OutQuint);
            }
        }

        protected override void Update()
        {
            base.Update();

            // The slider ball follows the path each frame while active - lazer updates its ball per-frame too.
            // Runs only while this object is within its lifetime (the container culls the rest).
            if (sliderBall != null && hitObject.Duration > 0)
            {
                double t = Time.Current;
                double start = hitObject.StartTime;
                if (t >= start && t <= start + hitObject.Duration)
                {
                    sliderBall.Position = ballPosition(t, start);
                    updateTickFeedback(t, start);
                }
                else
                {
                    lastBallArc = -1f; // reset so re-entering the slider doesn't fire a spurious crossing
                }

                updateTickVisibility(t, start);
                updateReverseArrows(t, start);
            }

            // Decay and apply the tick "pop": the follow circle briefly expands as the ball passes a tick.
            // Abs(Elapsed) so it also decays while scrubbing backwards.
            if (followCircle != null)
            {
                if (tickPulse > 0)
                    tickPulse = Math.Clamp(tickPulse - (float)(Math.Abs(Time.Elapsed) / tick_pulse_ms), 0f, 1f);
                followCircle.Scale = new Vector2(1f + tick_pulse_amp * tickPulse);
            }
        }

        /// <summary>Triggers the follow-circle pop whenever the ball's arc position crosses a tick this frame.</summary>
        private void updateTickFeedback(double time, double start)
        {
            float arc = ballParam(time, start) * totalLength;

            if (lastBallArc >= 0 && tickArcLengths.Length > 0)
            {
                float lo = Math.Min(lastBallArc, arc);
                float hi = Math.Max(lastBallArc, arc);
                foreach (float d in tickArcLengths)
                {
                    if (d > lo && d <= hi)
                    {
                        tickPulse = 1f;
                        break;
                    }
                }
            }

            lastBallArc = arc;
        }

        /// <summary>
        /// Fades each tick out as the ball sweeps over it on the current span, and back in when a repeat begins
        /// a fresh span - so a tick "disappears" the moment the ball reaches it. Computed from position (not a
        /// crossing event) so it stays correct while the playhead is scrubbed in either direction.
        /// </summary>
        private void updateTickVisibility(double time, double start)
        {
            if (tickDots.Count == 0)
                return;

            // How far along the path the ball has swept on the current span. Clamps mean that before the slider
            // starts the swept length is 0 (all ticks visible) and after it ends the final span is fully swept
            // (all ticks hidden).
            double spanDuration = hitObject.Duration / hitObject.Slides;
            double p = (time - start) / spanDuration;
            int span = Math.Clamp((int)p, 0, hitObject.Slides - 1);
            double frac = Math.Clamp(p - span, 0, 1);
            bool forward = span % 2 == 0;
            float swept = (float)(forward ? frac * totalLength : (1 - frac) * totalLength);

            // Each tick's alpha is a pure function of how far ahead it still is from the ball (in osu!pixels
            // along the sweep direction): full until the ball nears it, fading to 0 as the ball reaches/passes
            // it. Computing it from position (not an incremental lerp) means it's correct at any playhead -
            // it disappears the instant the ball arrives while scrubbing paused, and reappears on rewind.
            // The fade window is the arc the ball covers in tick_fade_ms (so the on-screen fade speed matches).
            float fadeArc = Math.Max(1f, (float)(totalLength * tick_fade_ms / spanDuration));

            for (int i = 0; i < tickDots.Count; i++)
            {
                float distAhead = forward ? tickArcLengths[i] - swept : swept - tickArcLengths[i];
                float fadeOut = Math.Clamp(distAhead / fadeArc, 0f, 1f);

                // Progressive appearance (like lazer's DrawableSliderTick): each tick fades in over
                // tick_fade_in_ms starting one preempt before its own time (its position along the first span),
                // so the ticks pop in one-by-one as the slider approaches instead of all at once. Past that
                // window (and on repeat spans) the factor is 1, leaving only the fade-out above.
                double tickTime = start + tickArcLengths[i] / totalLength * spanDuration;
                float fadeIn = (float)Math.Clamp((time - (tickTime - preempt)) / tick_fade_in_ms, 0, 1);

                tickDots[i].Alpha = tick_base_alpha * fadeOut * fadeIn;
            }
        }

        /// <summary>
        /// Fades each reverse arrow in only while the ball is heading toward that end and a reverse waits there
        /// (i.e. it's not the final span) - a port of lazer's behaviour where a repeat's arrow only appears near
        /// its own time and vanishes once hit. This keeps the head arrow off the combo number at slider start.
        /// </summary>
        private void updateReverseArrows(double time, double start)
        {
            if (reverseArrowHead == null && reverseArrowTail == null)
                return;

            int slides = hitObject.Slides;
            double spanDuration = hitObject.Duration / slides;

            // Which span the ball is on and which end it heads toward. Before the slider starts it is about to
            // run span 0 (forward, toward the tail); after it ends both arrows are gone.
            bool afterEnd = time > start + hitObject.Duration;
            int span;
            bool forward;
            if (time <= start)
            {
                span = 0;
                forward = true;
            }
            else
            {
                double p = (time - start) / spanDuration;
                span = Math.Clamp((int)p, 0, slides - 1);
                forward = span % 2 == 0;
            }

            // A reverse waits at the end of every span except the last; show the arrow at the end being approached.
            bool reverseAhead = !afterEnd && span < slides - 1;
            fadeArrow(reverseArrowTail, reverseAhead && forward);
            fadeArrow(reverseArrowHead, reverseAhead && !forward);
        }

        /// <summary>Lerps an arrow's alpha toward shown/hidden; Abs(Elapsed) keeps it smooth when scrubbing back.</summary>
        private void fadeArrow(Drawable? arrow, bool show)
        {
            if (arrow == null)
                return;

            float target = show ? 1f : 0f;
            float k = (float)Math.Clamp(Math.Abs(Time.Elapsed) / reverse_fade_ms, 0, 1);
            arrow.Alpha += (target - arrow.Alpha) * k;
        }

        private Vector2 ballPosition(double time, double start) => pointAtParam(ballParam(time, start));

        /// <summary>The ball's path parameter (0..1) at a time, bouncing back on odd spans.</summary>
        private float ballParam(double time, double start)
        {
            double spanDuration = hitObject.Duration / hitObject.Slides;
            double p = (time - start) / spanDuration;          // progress measured in spans
            int span = Math.Clamp((int)p, 0, hitObject.Slides - 1);
            float frac = (float)(p - span);
            float param = (span & 1) == 0 ? frac : 1 - frac;   // bounce back on odd spans
            return Math.Clamp(param, 0, 1);
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
