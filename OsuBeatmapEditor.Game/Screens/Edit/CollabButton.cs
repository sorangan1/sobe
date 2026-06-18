using System;
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
    /// Top-bar chip that opens the collab ("git for maps") panel. Lit in the accent colour while the open
    /// difficulty is linked to a server collab. Mirrors <see cref="ModdingModeButton"/>'s look.
    /// </summary>
    public partial class CollabButton : CircularContainer, IHasTooltip
    {
        private readonly BindableBool linked;
        private readonly Action onClick;

        private Box fill = null!;
        private SpriteText icon = null!;

        public LocalisableString TooltipText { get; }

        public CollabButton(BindableBool linked, Action onClick, string tooltip)
        {
            this.linked = linked;
            this.onClick = onClick;
            TooltipText = tooltip;
            Size = new Vector2(30);
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                fill = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface },
                icon = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    // ASCII glyph for "collab".
                    Text = "C",
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            };

            linked.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            fill.FadeColour(linked.Value ? EditorTheme.Colours.Accent : OsuColour.Surface, 120);
            icon.FadeColour(linked.Value ? OsuColour.BackgroundDark : OsuColour.Text, 120);
        }

        protected override bool OnClick(ClickEvent e)
        {
            onClick();
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!linked.Value)
                fill.FadeColour(OsuColour.BackgroundDark, 120);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            updateState();
            base.OnHoverLost(e);
        }
    }
}
