using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A square icon button for the song-select (main menu) action row: a rounded neutral surface that lightens
    /// on hover, with a centred icon and a tooltip. The icon can be swapped after construction (e.g. play/pause).
    /// </summary>
    public partial class MenuIconButton : ClickableContainer, IHasTooltip
    {
        private Box background = null!;
        private SpriteIcon iconSprite = null!;
        private Container content = null!;
        private IconUsage icon;
        private string tooltip;

        public MenuIconButton(IconUsage icon, string tooltip, Action onClick)
        {
            this.icon = icon;
            this.tooltip = tooltip;
            Action = onClick;
            Size = new Vector2(56);
        }

        public LocalisableString TooltipText => tooltip;

        /// <summary>Swaps the displayed icon (and its tooltip) - used to flip play ↔ pause.</summary>
        public void SetIcon(IconUsage newIcon, string newTooltip)
        {
            icon = newIcon;
            tooltip = newTooltip;
            if (iconSprite != null)
                iconSprite.Icon = newIcon;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Inner wrapper so the hover/press scale grows past the layout box (outer isn't masked) without
            // shifting neighbouring buttons in the action row.
            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = icon,
                        Size = new Vector2(20),
                        Colour = EditorTheme.Colours.Text,
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            content.ScaleTo(1.06f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            iconSprite.ScaleTo(1.08f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            content.ScaleTo(1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            iconSprite.ScaleTo(1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.9f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(IsHovered ? 1.06f : 1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }
    }
}
