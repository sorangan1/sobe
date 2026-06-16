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

        private bool enabled = true;

        /// <summary>Greys the chip out and ignores clicks (e.g. HD/HR while Modding Mode forces Auto-only).</summary>
        public void SetEnabled(bool value)
        {
            if (enabled == value)
                return;

            enabled = value;
            this.FadeTo(value ? 1f : 0.35f, 120);
        }

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
            if (!enabled)
                return true;

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
