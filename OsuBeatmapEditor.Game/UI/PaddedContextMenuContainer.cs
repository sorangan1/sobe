using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuBeatmapEditor.Game.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A basic context menu container whose items carry a little extra padding around their text.
    /// </summary>
    public partial class PaddedContextMenuContainer : BasicContextMenuContainer
    {
        protected override Menu CreateMenu() => new PaddedMenu();

        private partial class PaddedMenu : BasicMenu
        {
            public PaddedMenu()
                : base(Direction.Vertical)
            {
            }

            protected override Menu CreateSubMenu() => new PaddedMenu();

            protected override DrawableMenuItem CreateDrawableMenuItem(MenuItem item) => new PaddedDrawableMenuItem(item);

            private partial class PaddedDrawableMenuItem : BasicDrawableMenuItem
            {
                public PaddedDrawableMenuItem(MenuItem item)
                    : base(item)
                {
                }

                // The base content uses a relatively-sized, truncating label, which collapses to a thin
                // clipped rectangle inside an auto-sizing menu. Provide a self-sized label instead so the
                // item measures its full width and height.
                protected override Drawable CreateContent() => new SpriteText
                {
                    Text = Item.Text.Value,
                    Truncate = false,
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 17),
                    Margin = new MarginPadding { Horizontal = 14, Vertical = 7 },
                };
            }
        }
    }
}
