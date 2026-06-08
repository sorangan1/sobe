using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Colour;
using OsuBeatmapEditor.Game.Beatmaps;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Builds the dimmed background image shown behind a carousel card, fading from a transparent left
    /// edge (where the text sits) to the visible image on the right - matching osu!lazer's panels.
    /// </summary>
    public static class CarouselBackground
    {
        public static Drawable? TryCreate(LargeTextureStore? textures, BeatmapSetModel set, string backgroundFile)
        {
            if (textures == null || backgroundFile.Length == 0
                || !set.Files.TryGetValue(backgroundFile.ToLowerInvariant(), out string? hash) || hash.Length < 2)
                return null;

            var texture = textures.Get($"{hash[..1]}/{hash[..2]}/{hash}");
            if (texture == null)
                return null;

            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Children = new Drawable[]
                {
                    new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill,
                        Texture = texture,
                    },
                    // Left-to-right dim so the title/subtitle stays readable over the image.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(
                            new Color4(0f, 0f, 0f, 0.85f),
                            new Color4(0f, 0f, 0f, 0.35f)),
                    },
                },
            };
        }
    }
}
