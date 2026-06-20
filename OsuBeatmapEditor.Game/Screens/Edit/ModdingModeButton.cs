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

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small text toggle in the editor's top-left panel (next to the Patterns button, below the hitsound
    /// toggles). Toggles Modding Mode - the osu! discussion-review view. Lit in the accent colour when active.
    /// Styled to match the neutral <see cref="UI.OsuButton"/> next to it (same rounded-rect shape and font).
    /// </summary>
    public partial class ModdingModeButton : Container, IHasTooltip
    {
        private readonly BindableBool active;

        private Box fill = null!;
        private SpriteText label = null!;
        private bool hovered;

        public LocalisableString TooltipText { get; }

        public ModdingModeButton(BindableBool active, string tooltip)
        {
            this.active = active;
            TooltipText = tooltip;
            Size = new Vector2(80, 26);
            Masking = true;
            CornerRadius = 5;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                fill = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Modding",
                    Colour = EditorTheme.Colours.Text,
                    Font = FontUsage.Default.With(size: 12, weight: "SemiBold"),
                },
            };

            active.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            fill.FadeColour(active.Value ? EditorTheme.Colours.Accent
                : hovered ? EditorTheme.Colours.ControlHover : EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            label.FadeColour(active.Value ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
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
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hovered = false;
            updateState();
            base.OnHoverLost(e);
        }
    }
}
