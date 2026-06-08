using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// osu!lazer-style difficulty display: a rounded pill coloured by star rating, containing a star
    /// icon and the numeric rating.
    /// </summary>
    public partial class StarRatingDisplay : CompositeDrawable
    {
        public StarRatingDisplay(double stars)
        {
            AutoSizeAxes = Axes.Both;

            Color4 colour = StarRatingColour.For(stars);
            Color4 content = StarRatingColour.ContentColour(stars);

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colour,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.X,
                        Height = 20,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(4, 0),
                        Padding = new MarginPadding { Horizontal = 8 },
                        Children = new Drawable[]
                        {
                            new StarIcon(12, content)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = stars.ToString("0.00"),
                                Colour = content,
                                Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                            },
                        },
                    },
                },
            };
        }

        /// <summary>A small five-pointed star rendered as a vector outline (no font glyph).</summary>
        private partial class StarIcon : CompositeDrawable
        {
            public StarIcon(float size, Color4 colour)
            {
                Size = new Vector2(size);

                float outer = size / 2f;
                float inner = outer * 0.45f;

                var vertices = new List<Vector2>();
                for (int i = 0; i <= 10; i++)
                {
                    float angle = MathF.PI / 180f * (-90 + i * 36);
                    float r = i % 2 == 0 ? outer : inner;
                    vertices.Add(new Vector2(outer + r * MathF.Cos(angle), outer + r * MathF.Sin(angle)));
                }

                // A thick path whose tube reaches the centre, giving a solid (filled) star.
                var path = new SmoothPath
                {
                    PathRadius = size * 0.2f,
                    Colour = colour,
                };
                path.Vertices = vertices;
                path.Position = -path.PositionInBoundingBox(Vector2.Zero);

                InternalChild = path;
            }
        }
    }
}
