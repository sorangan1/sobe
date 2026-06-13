using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.Star,
                                Size = new Vector2(12),
                                Colour = content,
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
    }
}
