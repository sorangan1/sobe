using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Graphics
{
    /// <summary>
    /// Maps a star rating to a colour along osu!'s difficulty spectrum (blue -> green -> yellow ->
    /// red -> purple -> black), matching the look of osu!lazer's difficulty display.
    /// </summary>
    public static class StarRatingColour
    {
        private static readonly (double stars, Color4 colour)[] spectrum =
        {
            (0.1, fromHex(0x4290FB)),
            (1.25, fromHex(0x4FC0FF)),
            (2.0, fromHex(0x4FFFD5)),
            (2.5, fromHex(0x7CFF4F)),
            (3.3, fromHex(0xF6F05C)),
            (4.2, fromHex(0xFF8068)),
            (4.9, fromHex(0xFF4E6F)),
            (5.8, fromHex(0xC645B8)),
            (6.7, fromHex(0x6563DE)),
            (7.7, fromHex(0x18158E)),
            (9.0, fromHex(0x000000)),
        };

        public static Color4 For(double stars)
        {
            if (stars <= spectrum[0].stars)
                return spectrum[0].colour;

            for (int i = 1; i < spectrum.Length; i++)
            {
                if (stars <= spectrum[i].stars)
                {
                    var (s0, c0) = spectrum[i - 1];
                    var (s1, c1) = spectrum[i];
                    float t = (float)((stars - s0) / (s1 - s0));
                    return lerp(c0, c1, t);
                }
            }

            return spectrum[^1].colour;
        }

        /// <summary>
        /// Readable text/icon colour to place on top of the difficulty colour pill. For the highest
        /// (dark purple/black) tiers osu! switches the star and number to gold, like osu!lazer.
        /// </summary>
        public static Color4 ContentColour(double stars) => stars >= 6.5 ? fromHex(0xFFD23F) : new Color4(26, 26, 46, 255);

        private static Color4 lerp(Color4 a, Color4 b, float t) => new Color4(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            1);

        private static Color4 fromHex(uint rgb) => new Color4(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF),
            (byte)0xFF);
    }
}
