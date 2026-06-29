using osu.Framework.Allocation;
using osu.Framework.Bindables;
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
    /// A compact square icon button bound to a <see cref="BindableBool"/> toggle: neutral at rest, lit in the
    /// accent colour while active, with hover/press feedback and a tooltip. Used for the editor's bottom-left
    /// mode toggles (Modding / Review) so they read as icons rather than wide text chips.
    /// </summary>
    public partial class IconToggleButton : ClickableContainer, IHasTooltip
    {
        private readonly BindableBool active;
        private readonly IconUsage icon;

        private Box background = null!;
        private SpriteIcon iconSprite = null!;
        private Container content = null!;
        private bool hovered;

        public LocalisableString TooltipText { get; }

        public IconToggleButton(BindableBool active, IconUsage icon, string tooltip, float size = 30)
        {
            this.active = active;
            this.icon = icon;
            TooltipText = tooltip;
            Size = new Vector2(size);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
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
                        Size = new Vector2(14),
                        Colour = EditorTheme.Colours.TextMuted,
                    },
                },
            };

            active.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            background.FadeColour(active.Value ? EditorTheme.Colours.Accent
                : hovered ? EditorTheme.Colours.ControlHover : EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            iconSprite.FadeColour(active.Value ? EditorTheme.Colours.Sunken
                : hovered ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            active.Value = !active.Value;
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            hovered = true;
            updateState();
            content.ScaleTo(1.1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState();
            content.ScaleTo(1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            content.ScaleTo(0.88f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            content.ScaleTo(IsHovered ? 1.1f : 1f, EditorTheme.Motion.Normal, Easing.OutBack);
            base.OnMouseUp(e);
        }
    }
}
