using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A context-menu item that also carries an icon (and an optional "destructive" flag that tints it red),
    /// rendered by <see cref="PaddedContextMenuContainer"/>. Plain <see cref="MenuItem"/>s still work and
    /// simply render without an icon.
    /// </summary>
    public class IconMenuItem : MenuItem
    {
        public IconUsage Icon { get; }
        public bool Destructive { get; }

        public IconMenuItem(LocalisableString text, IconUsage icon, Action action, bool destructive = false)
            : base(text, action)
        {
            Icon = icon;
            Destructive = destructive;
        }
    }

    /// <summary>
    /// Context-menu container styled to the editor design system: a rounded, elevated "overlay" surface with
    /// comfortably padded items that highlight on hover and show a leading icon. Destructive actions (delete)
    /// are tinted red so they stand out from the rest.
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
                // Icon column width, so every label starts at the same x whether or not its row has an icon.
                private const float icon_size = 14f;

                public ThemedDrawableMenuItem(MenuItem item)
                    : base(item)
                {
                    BackgroundColour = EditorTheme.Colours.Overlay;
                    BackgroundColourHover = EditorTheme.Colours.ControlHover;
                    // The icon/label carry their own colours (incl. red for destructive items), so neutralise the
                    // base's foreground tint - otherwise hover would recolour the whole row a single flat colour.
                    ForegroundColour = ForegroundColourHover = osuTK.Graphics.Color4.White;
                }

                /// <summary>
                /// A self-sized, themed row (leading icon + label). Crucially the comfortable padding lives
                /// <b>inside</b> this auto-sizing content so it counts toward the width the menu measures
                /// (<c>ContentDrawWidth</c>); with the padding as an outer margin the menu sized to the bare text
                /// and the right side of each label spilled past the panel edge and got clipped.
                /// </summary>
                protected override Drawable CreateContent()
                {
                    var iconItem = Item as IconMenuItem;
                    // Destructive actions (delete) read in the error colour; everything else in the normal text colour.
                    var colour = iconItem?.Destructive == true ? EditorTheme.Colours.Error : EditorTheme.Colours.Text;

                    var children = new List<Drawable>();

                    if (iconItem != null)
                        children.Add(new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = iconItem.Icon,
                            Size = new Vector2(icon_size),
                            Colour = colour,
                        });

                    children.Add(new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Truncate = false,
                        Text = Item.Text.Value,
                        Colour = colour,
                        Font = EditorTheme.Type.Body(),
                    });

                    return new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                        Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Md },
                        Children = children.ToArray(),
                    };
                }
            }
        }
    }
}
