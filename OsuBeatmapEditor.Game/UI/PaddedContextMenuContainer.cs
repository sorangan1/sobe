using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuBeatmapEditor.Game.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Context-menu container styled to the editor design system: a rounded, elevated "overlay" surface
    /// with comfortably padded items that highlight on hover.
    /// </summary>
    public partial class PaddedContextMenuContainer : BasicContextMenuContainer
    {
        protected override Menu CreateMenu() => new ThemedMenu();

        private partial class ThemedMenu : BasicMenu
        {
            public ThemedMenu()
                : base(Direction.Vertical)
            {
                BackgroundColour = EditorTheme.Colours.Overlay;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                MaskingContainer.Masking = true;
                MaskingContainer.CornerRadius = EditorTheme.Radius.Md;
                MaskingContainer.BorderThickness = EditorTheme.Sizing.BorderThickness;
                MaskingContainer.BorderColour = EditorTheme.Colours.Border;
            }

            protected override Menu CreateSubMenu() => new ThemedMenu();

            protected override DrawableMenuItem CreateDrawableMenuItem(MenuItem item) => new ThemedDrawableMenuItem(item);

            private partial class ThemedDrawableMenuItem : BasicDrawableMenuItem
            {
                public ThemedDrawableMenuItem(MenuItem item)
                    : base(item)
                {
                    BackgroundColour = EditorTheme.Colours.Overlay;
                    BackgroundColourHover = EditorTheme.Colours.ControlHover;
                }

                // A self-sized, themed label (the base uses a relatively-sized, truncating one that collapses
                // inside an auto-sizing menu).
                protected override Drawable CreateContent() => new SpriteText
                {
                    Truncate = false,
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Body(),
                    Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Md },
                };
            }
        }
    }
}
