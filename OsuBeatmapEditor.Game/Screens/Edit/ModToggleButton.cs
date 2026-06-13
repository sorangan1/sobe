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
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small osu!-style mod "chip": a rounded square showing the mod acronym (e.g. "HR", "HD") in the
    /// mod's signature colour. Clicking toggles the bound state; when active it lights up in the mod colour,
    /// when off it sits dim with a coloured outline. Used in the editor to preview a map under a mod.
    /// </summary>
    public partial class ModToggleButton : CircularContainer, IHasTooltip
    {
        private readonly BindableBool active;
        private readonly string acronym;
        private readonly Color4 accent;

        public LocalisableString TooltipText { get; }

        private Box fill = null!;
        private SpriteText label = null!;

        public ModToggleButton(BindableBool active, string acronym, Color4 accent, string tooltip)
        {
            this.active = active;
            this.acronym = acronym;
            this.accent = accent;
            TooltipText = tooltip;

            Size = new Vector2(38, 30);
            Masking = true;
            CornerRadius = 7;
            BorderThickness = 2;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                fill = new Box { RelativeSizeAxes = Axes.Both },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = acronym,
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            };

            active.BindValueChanged(_ => updateState(), true);
        }

        private bool hovered;

        private void updateState()
        {
            if (active.Value)
            {
                BorderColour = accent;
                fill.FadeColour(accent, 120);
                label.FadeColour(OsuColour.BackgroundDark, 120);
            }
            else
            {
                BorderColour = accent;
                fill.FadeColour(hovered ? OsuColour.BackgroundRaised : OsuColour.Surface, 120);
                label.FadeColour(accent, 120);
            }
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
