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
    /// Compact usage-statistics readout for the main menu: total time the editor has been open and total
    /// active editing time across all maps. Updates live each frame.
    /// </summary>
    public partial class StatisticsDisplay : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private StatRow openRow = null!;
        private StatRow activeRow = null!;

        public StatisticsDisplay()
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
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    openRow = new StatRow("Editor open"),
                    activeRow = new StatRow("Active editing"),
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (statistics == null)
            {
                Alpha = 0;
                return;
            }

            Alpha = 1;
            openRow.Value = StatisticsTracker.Format(statistics.TotalOpenMs);
            activeRow.Value = StatisticsTracker.Format(statistics.TotalActiveMs);
        }

        private partial class StatRow : CompositeDrawable
        {
            private readonly SpriteText valueText;

            public StatRow(string label)
            {
                AutoSizeAxes = Axes.Both;
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = label,
                            Colour = OsuColour.TextMuted,
                            Font = FontUsage.Default.With(size: 13, weight: "SemiBold"),
                        },
                        valueText = new SpriteText
                        {
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 13, weight: "Bold", fixedWidth: true),
                        },
                    },
                };
            }

            public string Value
            {
                set => valueText.Text = value;
            }
        }
    }
}
