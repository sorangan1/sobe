using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Graphics
{
    /// <summary>
    /// The single source of truth for the editor's visual design tokens: colour, typography, spacing,
    /// corner radii, motion and control sizing. The aesthetic is a compact, neutral "pro-tool" dark theme
    /// (osu!lazer familiarity + IDE/dev-tool restraint): hierarchy comes from subtle surface contrast, not
    /// from chrome or saturated fills, and saturated colour is reserved for functional meaning.
    ///
    /// Always reference these tokens from UI code instead of hard-coding hex/sizes/radii/durations, so the
    /// look stays consistent and a single edit re-themes everything. See docs/design-guide.md for usage rules.
    /// (<see cref="OsuColour"/> is the legacy palette; it is being folded into this theme during the remodel.)
    /// </summary>
    public static class EditorTheme
    {
        private static Color4 hex(uint rgb, float alpha = 1f) => new Color4(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            alpha);

        /// <summary>
        /// The neutral dark surface/text scale plus brand accent and the functional (semantic) palette.
        /// Surfaces step from "sunken" (deepest troughs) up through panels to "overlay" (popovers/menus);
        /// pick the level by elevation, not by guesswork. Saturated entries carry fixed meaning.
        /// </summary>
        public static class Colours
        {
            // --- Surfaces (low → high elevation). Neutral grey, a whisper cool, no purple tint. ---

            /// <summary>Deepest troughs: playfield void, timeline backdrops, sunken wells (#101013).</summary>
            public static readonly Color4 Sunken = hex(0x101013);

            /// <summary>App background behind all chrome (#18181B).</summary>
            public static readonly Color4 Base = hex(0x18181B);

            /// <summary>Panels, toolbars, docked surfaces (#1F1F23).</summary>
            public static readonly Color4 Surface = hex(0x1F1F23);

            /// <summary>Raised blocks inside a panel; section cards (#27272C).</summary>
            public static readonly Color4 Raised = hex(0x27272C);

            /// <summary>Floating layers: popovers, menus, modals (#2E2E34).</summary>
            public static readonly Color4 Overlay = hex(0x2E2E34);

            // --- Controls (idle → hover → active). For buttons, inputs, rows. ---

            /// <summary>Idle interactive control fill (#303036).</summary>
            public static readonly Color4 Control = hex(0x303036);

            /// <summary>Hovered control fill (#3A3A42).</summary>
            public static readonly Color4 ControlHover = hex(0x3A3A42);

            /// <summary>Pressed / selected (non-accent) control fill (#45454F).</summary>
            public static readonly Color4 ControlActive = hex(0x45454F);

            // --- Lines ---

            /// <summary>Hairline dividers and control borders (#3A3A42).</summary>
            public static readonly Color4 Border = hex(0x3A3A42);

            /// <summary>Stronger separators / focused borders (#52525E).</summary>
            public static readonly Color4 BorderStrong = hex(0x52525E);

            // --- Text (primary → tertiary). ---

            /// <summary>Primary text (#ECECEF).</summary>
            public static readonly Color4 Text = hex(0xECECEF);

            /// <summary>Secondary / label text (#A0A0AC).</summary>
            public static readonly Color4 TextMuted = hex(0xA0A0AC);

            /// <summary>Tertiary / disabled text and faint meta (#6E6E7A).</summary>
            public static readonly Color4 TextFaint = hex(0x6E6E7A);

            // --- Brand accent (osu! pink). Used sparingly: primary action, focus, brand moments. ---

            /// <summary>Signature osu! pink, the single brand accent (#FF66AB).</summary>
            public static readonly Color4 Accent = hex(0xFF66AB);

            /// <summary>Hovered accent (#FF7DB7).</summary>
            public static readonly Color4 AccentHover = hex(0xFF7DB7);

            /// <summary>Pressed accent (#E0568F).</summary>
            public static readonly Color4 AccentPressed = hex(0xE0568F);

            /// <summary>Accent at low alpha for tints, fills behind accent text, focus halos (#FF66AB @ 0.16).</summary>
            public static readonly Color4 AccentSoft = hex(0xFF66AB, 0.16f);

            // --- Functional / semantic palette. Saturated colour ONLY here, each with a fixed meaning. ---

            /// <summary>Uninherited (red) timing points / BPM (#FF5C6C).</summary>
            public static readonly Color4 Timing = hex(0xFF5C6C);

            /// <summary>Inherited (green) timing points / slider velocity (#52D38C).</summary>
            public static readonly Color4 Velocity = hex(0x52D38C);

            /// <summary>Selection highlight: rings, drag boxes, selected markers (#FFC93C).</summary>
            public static readonly Color4 Selection = hex(0xFFC93C);

            /// <summary>Kiai sections (#FF9D4D).</summary>
            public static readonly Color4 Kiai = hex(0xFF9D4D);

            /// <summary>Bookmarks / informational markers (#4FB3FF).</summary>
            public static readonly Color4 Bookmark = hex(0x4FB3FF);

            /// <summary>Default (no-skin) slider-body fill: a fixed dark grey, so the body doesn't take the combo colour (#37373D).</summary>
            public static readonly Color4 SliderBodyDefault = hex(0x37373D);

            // --- Status (toasts, validation, confirmations). ---

            /// <summary>Success / confirmation (#46C77F).</summary>
            public static readonly Color4 Success = hex(0x46C77F);

            /// <summary>Warning / caution (#FFB454).</summary>
            public static readonly Color4 Warning = hex(0xFFB454);

            /// <summary>Error / destructive (#FF5C5C).</summary>
            public static readonly Color4 Error = hex(0xFF5C5C);

            /// <summary>Neutral info (#4FB3FF).</summary>
            public static readonly Color4 Info = hex(0x4FB3FF);
        }

        /// <summary>
        /// The type scale. Compact and few steps; pick by role, not by eyeballing a pixel size. The framework's
        /// default font is used throughout; the <c>numeric</c> argument requests fixed-width digits for values
        /// that update live (times, BPM, counts) so they don't jitter as the digits change.
        /// </summary>
        public static class Type
        {
            private static FontUsage f(float size, string weight, bool numeric = false) =>
                FontUsage.Default.With(size: size, weight: weight, fixedWidth: numeric);

            /// <summary>22 / Bold — rare, the largest header (e.g. a modal's main title).</summary>
            public static FontUsage Display(bool numeric = false) => f(22, "Bold", numeric);

            /// <summary>18 / Bold — overlay and panel titles.</summary>
            public static FontUsage Title(bool numeric = false) => f(18, "Bold", numeric);

            /// <summary>16 / SemiBold — section headings, prominent values.</summary>
            public static FontUsage Heading(bool numeric = false) => f(16, "SemiBold", numeric);

            /// <summary>14 / Regular — default UI body text.</summary>
            public static FontUsage Body(bool numeric = false) => f(14, "Regular", numeric);

            /// <summary>14 / SemiBold — emphasised body (button text, active labels).</summary>
            public static FontUsage BodyStrong(bool numeric = false) => f(14, "SemiBold", numeric);

            /// <summary>13 / SemiBold — control labels, chips, tabs.</summary>
            public static FontUsage Label(bool numeric = false) => f(13, "SemiBold", numeric);

            /// <summary>11 / Regular — captions, faint meta, pill readouts.</summary>
            public static FontUsage Caption(bool numeric = false) => f(11, "Regular", numeric);
        }

        /// <summary>
        /// Spacing scale (px), a 4-unit rhythm tuned compact. Use for padding, gaps and margins; gaps between
        /// peers use Sm/Md, padding inside controls uses Sm/Md, panel insets use Lg/Xl.
        /// </summary>
        public static class Spacing
        {
            public const float Xxs = 2f;
            public const float Xs = 4f;
            public const float Sm = 6f;
            public const float Md = 8f;
            public const float Lg = 12f;
            public const float Xl = 16f;
            public const float Xxl = 24f;
        }

        /// <summary>Corner radii (px). One small step for controls, one for panels, plus fully-round.</summary>
        public static class Radius
        {
            /// <summary>Inputs, swatches, chips, small tags (3).</summary>
            public const float Sm = 3f;

            /// <summary>Buttons, rows, tabs (5).</summary>
            public const float Md = 5f;

            /// <summary>Panels, overlays, modals (8).</summary>
            public const float Lg = 8f;

            /// <summary>Fully round (use on a CircularContainer); dots, pills, the seek playhead.</summary>
            public const float Pill = 9999f;
        }

        /// <summary>
        /// Motion tokens: durations (ms) + the house easing. Keep transitions short and consistent — a
        /// pro-tool should feel immediate. <see cref="Easing.OutQuint"/> is the default curve everywhere
        /// (matches osu!lazer and the existing editor code).
        /// </summary>
        public static class Motion
        {
            /// <summary>Hover in/out, press feedback (80 ms).</summary>
            public const double Fast = 80;

            /// <summary>State changes: active highlight, toggle, selection (150 ms).</summary>
            public const double Normal = 150;

            /// <summary>Overlay / modal pop in-out, larger reveals (300 ms).</summary>
            public const double Slow = 300;

            /// <summary>The default easing curve for all UI transitions.</summary>
            public const Easing Ease = Easing.OutQuint;
        }

        /// <summary>
        /// Standard control dimensions (px) for the compact density. Heights keep rows on a shared baseline;
        /// use these instead of per-component magic numbers so controls line up across panels.
        /// </summary>
        public static class Sizing
        {
            /// <summary>Height of a labeled settings row.</summary>
            public const float RowHeight = 30f;

            /// <summary>Height of a standard button.</summary>
            public const float ButtonHeight = 28f;

            /// <summary>Height of a text/number input.</summary>
            public const float InputHeight = 26f;

            /// <summary>Hit-target minimum for any interactive element (accessibility floor).</summary>
            public const float MinTouchTarget = 24f;

            /// <summary>Standard hairline thickness for borders/dividers.</summary>
            public const float BorderThickness = 1f;

            /// <summary>Fixed width of a modal overlay window. All editor modals share one size.</summary>
            public const float OverlayWidth = 800f;

            /// <summary>Fixed height of a modal overlay window. All editor modals share one size.</summary>
            public const float OverlayHeight = 540f;
        }
    }
}
