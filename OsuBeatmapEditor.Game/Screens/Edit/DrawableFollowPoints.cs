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

        private readonly List<Point> points = new List<Point>();

        public DrawableFollowPoints(Vector2 start, Vector2 end, double startTime, double endTime)
        {
            RelativeSizeAxes = Axes.Both;

            Vector2 delta = end - start;
            float distance = delta.Length;
            if (distance < spacing * 2)
                return;

            float rotation = MathHelper.RadiansToDegrees((float)Math.Atan2(delta.Y, delta.X));
            double duration = Math.Max(1, endTime - startTime);

            for (float d = spacing * 1.5f; d < distance - spacing; d += spacing)
            {
                float fraction = d / distance;
                Vector2 from = start + (fraction - 0.1f) * delta;
                Vector2 to = start + fraction * delta;

                double fadeOutTime = startTime + fraction * duration;
                double fadeInTime = fadeOutTime - preempt;

                var arrow = new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Position = to,
                    Rotation = rotation,
                    Text = ">",
                    Colour = new Color4(1f, 1f, 1f, 0.65f),
                    Font = FontUsage.Default.With(size: 18, weight: "Bold"),
                    Alpha = 0,
                };

                AddInternal(arrow);
                points.Add(new Point(arrow, from, to, fadeInTime, fadeOutTime));
            }
        }

        /// <summary>Updates every arrow's fade/position for the given playback time.</summary>
        public void UpdateAt(double time)
        {
            foreach (var p in points)
            {
                if (time < p.FadeInTime || time > p.FadeOutTime)
                {
                    p.Arrow.Alpha = 0;
                    continue;
                }

                double sinceIn = time - p.FadeInTime;
                float inAlpha = (float)Math.Clamp(sinceIn / fade, 0, 1);
                float outAlpha = (float)Math.Clamp((p.FadeOutTime - time) / fade, 0, 1);
                p.Arrow.Alpha = inAlpha * outAlpha;

                float move = (float)Math.Clamp(sinceIn / fade, 0, 1);
                p.Arrow.Position = Vector2.Lerp(p.From, p.To, 1 - (1 - move) * (1 - move)); // ease-out
            }
        }

        private readonly record struct Point(SpriteText Arrow, Vector2 From, Vector2 To, double FadeInTime, double FadeOutTime);
    }
}
