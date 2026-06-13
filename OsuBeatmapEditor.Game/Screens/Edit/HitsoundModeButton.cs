using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Small circular button at the bottom-left of the editor (above the background toggle). Clicking it
    /// opens/closes the expanded hitsound-lanes editor in the top timeline: the timeline grows three rows
    /// (Clap / Whistle / Finish) synced with the notes and the playfield shrinks to make room.
    /// </summary>
    public partial class HitsoundModeButton : CircularContainer
    {
        private readonly BindableBool active;

        private Box fill = null!;
        private SpriteText icon = null!;

        public HitsoundModeButton(BindableBool active)
        {
            this.active = active;
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
                    // ASCII glyph evoking stacked hitsound rows.
                    Text = "=",
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 15, weight: "Bold"),
                },
            };

            active.BindValueChanged(_ => updateState(), true);
        }

        private void updateState()
        {
            fill.FadeColour(active.Value ? OsuColour.Pink : OsuColour.Surface, 120);
            icon.FadeColour(active.Value ? OsuColour.BackgroundDark : OsuColour.Text, 120);
        }

        protected override bool OnClick(ClickEvent e)
        {
            active.Value = !active.Value;
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
