using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Statistics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// In-editor readout of editing time for the current map: total time edited (all sessions) and the
    /// current editing session. Sits in the bottom-right corner, just above the FPS counter.
    /// </summary>
    public partial class MapStatsDisplay : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private SpriteText totalText = null!;
        private SpriteText sessionText = null!;

        public MapStatsDisplay()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    totalText = line(),
                    sessionText = line(),
                },
            };
        }

        private static SpriteText line() => new SpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Colour = OsuColour.TextMuted,
            Font = FontUsage.Default.With(size: 13, weight: "SemiBold", fixedWidth: true),
        };

        protected override void Update()
        {
            base.Update();

            if (statistics == null)
            {
                Alpha = 0;
                return;
            }

            Alpha = 1;
            totalText.Text = $"Total edited: {StatisticsTracker.Format(statistics.ActiveMsForCurrentMap)}";
            sessionText.Text = $"This session: {StatisticsTracker.Format(statistics.CurrentSessionActiveMs)}";
        }
    }
}
