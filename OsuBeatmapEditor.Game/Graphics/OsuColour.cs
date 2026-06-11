using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Graphics
{
    /// <summary>
    /// Legacy palette shim. The named entries now forward to the design tokens in <see cref="EditorTheme"/>
    /// so the whole app shares one neutral pro-tool palette; new/edited UI should reference
    /// <see cref="EditorTheme"/> directly (see docs/design-guide.md). Only the beatmap-content colours
    /// (<see cref="ComboColours"/>, <see cref="Purple"/>) remain native here - they are map content, not chrome.
    /// </summary>
    public static class OsuColour
    {
        /// <summary>Brand accent (osu! pink). Maps to <see cref="EditorTheme.Colours.Accent"/>.</summary>
        public static readonly Color4 Pink = EditorTheme.Colours.Accent;

        /// <summary>Pressed accent. Maps to <see cref="EditorTheme.Colours.AccentPressed"/>.</summary>
        public static readonly Color4 PinkDark = EditorTheme.Colours.AccentPressed;

        /// <summary>Secondary purple accent (spinner decoration - map content, kept native) (#AF59FF).</summary>
        public static readonly Color4 Purple = fromHex(0xAF59FF);

        /// <summary>Selection highlight. Maps to <see cref="EditorTheme.Colours.Selection"/>.</summary>
        public static readonly Color4 Yellow = EditorTheme.Colours.Selection;

        /// <summary>Deepest surface. Maps to <see cref="EditorTheme.Colours.Sunken"/>.</summary>
        public static readonly Color4 BackgroundDark = EditorTheme.Colours.Sunken;

        /// <summary>Raised panel surface. Maps to <see cref="EditorTheme.Colours.Raised"/>.</summary>
        public static readonly Color4 BackgroundRaised = EditorTheme.Colours.Raised;

        /// <summary>Idle control surface. Maps to <see cref="EditorTheme.Colours.Control"/>.</summary>
        public static readonly Color4 Surface = EditorTheme.Colours.Control;

        /// <summary>Primary text. Maps to <see cref="EditorTheme.Colours.Text"/>.</summary>
        public static readonly Color4 Text = EditorTheme.Colours.Text;

        /// <summary>Muted text. Maps to <see cref="EditorTheme.Colours.TextMuted"/>.</summary>
        public static readonly Color4 TextMuted = EditorTheme.Colours.TextMuted;

        /// <summary>The four default combo colours, cycled per new combo (map content, kept native).</summary>
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
