using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A small, unobtrusive frames-per-second readout. The rate is exponentially smoothed so the value
    /// stays readable rather than flickering every frame.
    /// </summary>
    public partial class FpsCounter : Container
    {
        private SpriteText text = null!;
        private double averageFrameTime;

        public FpsCounter()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.6f },
                text = new SpriteText
                {
                    Margin = new MarginPadding { Horizontal = 8, Vertical = 4 },
                    Colour = OsuColour.TextMuted,
                    Font = FontUsage.Default.With(size: 14, weight: "Bold", fixedWidth: true),
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            double elapsed = Clock.ElapsedFrameTime;
            if (elapsed <= 0)
                return;

            // Exponential moving average over frame time, then invert to a rate.
            averageFrameTime = averageFrameTime == 0 ? elapsed : averageFrameTime * 0.9 + elapsed * 0.1;
            text.Text = $"{1000.0 / averageFrameTime:0} FPS";
        }
    }
}
