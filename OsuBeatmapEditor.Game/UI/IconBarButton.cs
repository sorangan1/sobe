using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Graphics.Cursor;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A compact square icon button for the editor's top-left bar (e.g. the settings gear), styled to match the
    /// neutral text buttons next to it: a rounded surface that lightens on hover. Shows a tooltip for its label.
    /// </summary>
    public partial class IconBarButton : ClickableContainer, IHasTooltip
    {
        private readonly IconUsage icon;
        private readonly string tooltip;
        private Box background = null!;

        public IconBarButton(IconUsage icon, string tooltip, Action onClick)
        {
            this.icon = icon;
            this.tooltip = tooltip;
            Action = onClick;
            Size = new Vector2(24);
            Masking = true;
            CornerRadius = 5;
        }

        public LocalisableString TooltipText => tooltip;

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(13),
                    Colour = EditorTheme.Colours.Text,
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) =>
            background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
    }
}
