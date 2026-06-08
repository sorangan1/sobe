using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Graphics
{
    /// <summary>
    /// Central palette for the editor, following the osu!lazer dark aesthetic.
    /// Kept as a small set of named constants so the look stays consistent across components.
    /// </summary>
    public static class OsuColour
    {
        /// <summary>The signature osu! pink accent (#FF66AB).</summary>
        public static readonly Color4 Pink = fromHex(0xFF66AB);

        /// <summary>A darker pink used for pressed / active states (#CC5088).</summary>
        public static readonly Color4 PinkDark = fromHex(0xCC5088);

        /// <summary>Secondary purple accent (#AF59FF).</summary>
        public static readonly Color4 Purple = fromHex(0xAF59FF);

        /// <summary>Selection highlight (#FFD23F).</summary>
        public static readonly Color4 Yellow = fromHex(0xFFD23F);

        /// <summary>Deep background, near-black with a blue/purple tint (#1A1A2E).</summary>
        public static readonly Color4 BackgroundDark = fromHex(0x1A1A2E);

        /// <summary>Slightly lighter background for raised surfaces / panels (#26263F).</summary>
        public static readonly Color4 BackgroundRaised = fromHex(0x26263F);

        /// <summary>Neutral surface used for idle buttons (#33334D).</summary>
        public static readonly Color4 Surface = fromHex(0x33334D);

        /// <summary>Primary text colour (#F0F0F5).</summary>
        public static readonly Color4 Text = fromHex(0xF0F0F5);

        /// <summary>Muted / secondary text colour (#9090A8).</summary>
        public static readonly Color4 TextMuted = fromHex(0x9090A8);

        /// <summary>The four default combo colours, cycled per new combo. Chosen to be vivid yet easy on the eyes.</summary>
        public static readonly Color4[] ComboColours =
        {
            fromHex(0xFF7FA3), // soft pink
            fromHex(0x66C7FF), // sky blue
            fromHex(0x7FE3A0), // mint green
            fromHex(0xFFCB6B), // warm gold
        };

        /// <summary>The combo colour for the given combo index (wraps around the four-colour palette).</summary>
        public static Color4 ComboColourFor(int comboIndex)
        {
            int i = ((comboIndex % ComboColours.Length) + ComboColours.Length) % ComboColours.Length;
            return ComboColours[i];
        }

        private static Color4 fromHex(uint rgb) => new Color4(
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF),
            (byte)0xFF);
    }
}
