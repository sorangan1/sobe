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
    /// <summary>The speed-up mod preview state cycled by <see cref="RateModButton"/>.</summary>
    public enum RateMod
    {
        Off,

        /// <summary>1.5x speed, pitch preserved (tempo adjustment).</summary>
        DoubleTime,

        /// <summary>1.5x speed, pitch raised (frequency adjustment).</summary>
        Nightcore,
    }

    /// <summary>
    /// A mod chip that cycles three states on click: off → DoubleTime → Nightcore → off, like osu!'s DT/NC
    /// toggle. The acronym and accent change with the state ("DT" then "NC"). State is exposed via a bindable
    /// the editor watches to drive the playback rate (DT keeps pitch, NC raises it).
    /// </summary>
    public partial class RateModButton : CircularContainer, IHasTooltip
    {
        private readonly Bindable<RateMod> state;
        private readonly Color4 dtColour;
        private readonly Color4 ncColour;

        public LocalisableString TooltipText { get; }

        private Box fill = null!;
        private SpriteText label = null!;
        private bool hovered;
        private bool enabled = true;

        /// <summary>Greys the chip out and ignores clicks (e.g. while Modding Mode forces Auto-only).</summary>
        public void SetEnabled(bool value)
        {
            if (enabled == value)
                return;

            enabled = value;
            this.FadeTo(value ? 1f : 0.35f, 120);
        }

        public RateModButton(Bindable<RateMod> state, Color4 dtColour, Color4 ncColour, string tooltip)
        {
            this.state = state;
            this.dtColour = dtColour;
            this.ncColour = ncColour;
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
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            };

            state.BindValueChanged(_ => updateState(), true);
        }

        private Color4 accentFor(RateMod s) => s == RateMod.Nightcore ? ncColour : dtColour;

        private void updateState()
        {
            // Nightcore shows "NC"; off and DoubleTime both show "DT" (off is just dimmed).
            label.Text = state.Value == RateMod.Nightcore ? "NC" : "DT";

            Color4 accent = accentFor(state.Value);
            BorderColour = accent;

            if (state.Value != RateMod.Off)
            {
                fill.FadeColour(accent, 120);
                label.FadeColour(OsuColour.BackgroundDark, 120);
            }
            else
            {
                fill.FadeColour(hovered ? OsuColour.BackgroundRaised : OsuColour.Surface, 120);
                label.FadeColour(accent, 120);
            }
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!enabled)
                return true;

            // off -> DoubleTime -> Nightcore -> off
            state.Value = state.Value switch
            {
                RateMod.Off => RateMod.DoubleTime,
                RateMod.DoubleTime => RateMod.Nightcore,
                _ => RateMod.Off,
            };
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
