using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The "follow point" link between two consecutive hit objects in the same combo. osu!lazer draws a chain
    /// of discrete skin arrow-frames, each fading in <c>PREEMPT</c> ms before its point on the line and out as
    /// the playback wave reaches it; the net effect is a band of frames sweeping from the first object toward
    /// the second and stopping short of it. Since we ship no skin, this reproduces the exact same timing with a
    /// single continuous line whose visible band travels A → B: at time <c>t</c> the band spans the same line
    /// fractions lazer's visible frames would (<c>[(t-start)/dur, (t-start+PREEMPT)/dur]</c>), so it appears
    /// about <c>PREEMPT</c> ms before the first object and lasts until the second - not a fraction of a second.
    /// Positions are in osu!pixel coordinates (Anchor.TopLeft), matching the playfield.
    /// </summary>
    public partial class DrawableFollowPoints : CompositeDrawable
    {
        // Lead-time (ms) the band appears ahead of the link. osu!lazer uses 800; we use less so far-off
        // objects' follow points don't show - only the immediately upcoming link(s).
        private const double preempt = 450;

        private const float thickness = 2f;
        private const float end_gap = 16f;  // osu!pixel inset: clears circle A and stops short of B (the "retract")
        // osu!lazer only emits follow points when the objects are far enough apart (its loop runs from
        // 1.5*SPACING to distance-SPACING, so nothing shows below ~2.5*SPACING). Mirror that minimum.
        private const float min_distance = 80f;
        private const double fade = 200;    // fade-in / fade-out duration (ms)
        private const int samples = 24;     // keyframes used to animate the band's (piecewise-linear) ends

        // Keep completed transforms so they re-evaluate correctly when the editor seeks (lazer overrides this).
        public override bool RemoveCompletedTransforms => false;

        // The LifetimeManagementContainer manages our lifetime; it must not remove us when we go out of it.
        public override bool RemoveWhenNotAlive => false;

        private readonly double startTime;
        private readonly double endTime;
        private readonly float minLocal;
        private readonly float maxLocal;

        private Container? band;

        public DrawableFollowPoints(Vector2 start, Vector2 end, double startTime, double endTime)
        {
            this.startTime = startTime;
            this.endTime = endTime;

            RelativeSizeAxes = Axes.Both;

            // No forward time gap (the target object starts at or before the source ends - e.g. a slider resized
            // long enough to overlap the next note). lazer draws no follow points across such a link, and the
            // band animation would otherwise compute negative transform durations and crash.
            if (endTime <= startTime)
            {
                LifetimeEnd = LifetimeStart;
                return;
            }

            Vector2 delta = end - start;
            float distance = delta.Length;
            if (distance < min_distance)
            {
                LifetimeEnd = LifetimeStart; // objects too close - lazer shows no follow points here
                return;
            }

            float rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(delta.Y, delta.X));
            minLocal = end_gap;
            maxLocal = distance - end_gap;

            var clear = new Color4(1f, 1f, 1f, 0f);
            var bright = new Color4(1f, 1f, 1f, 0.95f);

            // A container laid along the A→B line (local +X = the line direction). The travelling band is a
            // child whose X (trailing edge) and Width animate; its symmetric gradient keeps both ends soft, so
            // there is never a hard cut as it moves.
            AddInternal(new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                Position = start,
                Rotation = rotation,
                Size = new Vector2(distance, thickness),
                Child = band = new Container
                {
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Y,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Colour = ColourInfo.GradientHorizontal(clear, bright),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Colour = ColourInfo.GradientHorizontal(bright, clear),
                        },
                    },
                },
            });

            // Visible window matches lazer: from the earliest point's fade-in (~startTime - PREEMPT) until the
            // wave reaches the second object.
            LifetimeStart = startTime - preempt - fade;
            LifetimeEnd = endTime + fade;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (band == null || LifetimeEnd == LifetimeStart)
                return;

            double windowStart = startTime - preempt;

            // Fade the band in as it first appears and out as it reaches the second object.
            using (band.BeginAbsoluteSequence(windowStart))
                band.FadeIn(fade);
            using (band.BeginAbsoluteSequence(endTime - fade))
                band.FadeOut(fade);

            // Animate the band's trailing (X) and leading (X + Width) edges through the lazer fractions. Both
            // edges are piecewise-linear in time, so stepping with Easing.None reproduces them faithfully.
            band.X = trailAt(windowStart);
            band.Width = Math.Max(0, leadAt(windowStart) - band.X);

            for (int i = 0; i < samples; i++)
            {
                double t0 = windowStart + (endTime - windowStart) * i / samples;
                double t1 = windowStart + (endTime - windowStart) * (i + 1) / samples;
                float x = trailAt(t1);
                float w = Math.Max(0, leadAt(t1) - x);

                using (band.BeginAbsoluteSequence(t0))
                {
                    band.MoveToX(x, t1 - t0, Easing.None);
                    band.ResizeWidthTo(w, t1 - t0, Easing.None);
                }
            }
        }

        /// <summary>Local-X (osu!px along the line) of the band's trailing edge at time t (lazer's fade-out front).</summary>
        private float trailAt(double t) => localAt((t - startTime) / Math.Max(1, endTime - startTime));

        /// <summary>Local-X of the band's leading edge at time t (lazer's fade-in front, PREEMPT ahead).</summary>
        private float leadAt(double t) => localAt((t - startTime + preempt) / Math.Max(1, endTime - startTime));

        /// <summary>Maps a line fraction to a clamped local-X, honouring the start/end insets (the "retract").</summary>
        private float localAt(double fraction) =>
            Math.Clamp((float)fraction * (maxLocal + end_gap), minLocal, maxLocal);
    }
}
