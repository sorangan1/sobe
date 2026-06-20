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
    /// Top-bar chip that toggles "authorship colours": tinting each object by who placed it (from the collab's
    /// revision history). Lit in the accent colour while active. Mirrors <see cref="CollabButton"/>'s look.
    /// </summary>
    public partial class AuthorsButton : CircularContainer, IHasTooltip
    {
        private readonly BindableBool active;
        private readonly Action onClick;

        private Box fill = null!;
        private SpriteText icon = null!;

        public LocalisableString TooltipText { get; }

        public AuthorsButton(BindableBool active, Action onClick, string tooltip)
        {
            this.active = active;
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
                    // ASCII glyph for "authors".
                    Text = "A",
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            };

            active.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            fill.FadeColour(active.Value ? EditorTheme.Colours.Accent : OsuColour.Surface, 120);
            icon.FadeColour(active.Value ? OsuColour.BackgroundDark : OsuColour.Text, 120);
        }

        protected override bool OnClick(ClickEvent e)
        {
            onClick();
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!active.Value)
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
