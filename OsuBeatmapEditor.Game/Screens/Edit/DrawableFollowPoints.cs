using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The chain of small arrows ("follow points") drawn between two consecutive hit objects in the
    /// same combo, mirroring osu!lazer's <c>FollowPointConnection</c>: arrows are spaced evenly along
    /// the line, each fading in shortly before the playback wave reaches it and out as it passes.
    /// Positions are in osu!pixel coordinates (Anchor.TopLeft), matching the playfield.
    /// </summary>
    public partial class DrawableFollowPoints : CompositeDrawable
    {
        private const float spacing = 32f;     // osu!pixel gap between consecutive arrows
        private const double preempt = 800;    // how far ahead (ms) an arrow starts fading in
        private const double fade = 200;       // fade-in / fade-out duration (ms)

        // Keep completed transforms so they re-evaluate correctly when the editor seeks (lazer overrides this).
        public override bool RemoveCompletedTransforms => false;

        public DrawableFollowPoints(Vector2 start, Vector2 end, double startTime, double endTime)
        {
            RelativeSizeAxes = Axes.Both;

            Vector2 delta = end - start;
            float distance = delta.Length;
            if (distance < spacing * 2)
            {
                LifetimeEnd = LifetimeStart; // nothing to show
                return;
            }

            float rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(delta.Y, delta.X));
            double duration = Math.Max(1, endTime - startTime);

            double firstFadeIn = double.MaxValue;
            double lastFadeOut = double.MinValue;

            for (float d = spacing * 1.5f; d < distance - spacing; d += spacing)
            {
                float fraction = d / distance;
                Vector2 from = start + (fraction - 0.1f) * delta;
                Vector2 to = start + fraction * delta;

                double fadeOutTime = startTime + fraction * duration;
                double fadeInTime = fadeOutTime - preempt;

                var arrow = new FollowPointArrow
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = from,
                    Rotation = rotation,
                    Text = ">",
                    Colour = new Color4(1f, 1f, 1f, 0.65f),
                    Font = FontUsage.Default.With(size: 18, weight: "Bold"),
                    Alpha = 0,
                };

                // Fade in + ease toward the target as the playback wave approaches, fade out as it passes.
                using (arrow.BeginAbsoluteSequence(fadeInTime))
                {
                    arrow.FadeIn(fade);
                    arrow.MoveTo(to, fade, Easing.Out);
                }

                using (arrow.BeginAbsoluteSequence(fadeOutTime - fade))
                    arrow.FadeOut(fade);

                AddInternal(arrow);

                firstFadeIn = Math.Min(firstFadeIn, fadeInTime);
                lastFadeOut = Math.Max(lastFadeOut, fadeOutTime);
            }

            if (firstFadeIn <= lastFadeOut)
            {
                LifetimeStart = firstFadeIn;
                LifetimeEnd = lastFadeOut;
            }
        }

        /// <summary>A follow-point arrow that keeps its completed transforms so it re-evaluates when seeking.</summary>
        private partial class FollowPointArrow : SpriteText
        {
            public override bool RemoveCompletedTransforms => false;
        }
    }
}
