using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using OsuBeatmapEditor.Game.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A <see cref="BasicDropdown{T}"/> restyled to the editor design system: a rounded "control" header
    /// that lightens on hover, opening onto a rounded, elevated "overlay" list.
    /// </summary>
    public partial class ThemedDropdown<T> : BasicDropdown<T>
    {
        protected override DropdownHeader CreateHeader() => new ThemedHeader();

        protected override DropdownMenu CreateMenu() => new ThemedMenu();

        private partial class ThemedHeader : BasicDropdownHeader
        {
            public ThemedHeader()
            {
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
                BackgroundColour = EditorTheme.Colours.Surface;
                BackgroundColourHover = EditorTheme.Colours.ControlHover;
                Foreground.Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Sm };
            }
        }

        private partial class ThemedMenu : BasicDropdownMenu
        {
            public ThemedMenu()
            {
                BackgroundColour = EditorTheme.Colours.Overlay;
                MaskingContainer.CornerRadius = EditorTheme.Radius.Md;
                MaskingContainer.Masking = true;
            }
        }
    }
}
