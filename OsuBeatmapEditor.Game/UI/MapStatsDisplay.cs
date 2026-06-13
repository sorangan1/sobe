using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Statistics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// In-editor readout of editing time for the current map: total time edited (all sessions) and the
    /// current editing session. Sits in the bottom-right corner, just above the FPS counter, styled to match
    /// it (rounded translucent panel, muted label + brighter value).
    /// </summary>
    public partial class MapStatsDisplay : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private StatLine totalLine = null!;
        private StatLine sessionLine = null!;

        public MapStatsDisplay()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.6f },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Padding = new MarginPadding { Horizontal = 8, Vertical = 5 },
                    Children = new Drawable[]
                    {
                        totalLine = new StatLine("TOTAL"),
                        sessionLine = new StatLine("SESSION"),
                    },
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
            totalLine.Value = StatisticsTracker.Format(statistics.ActiveMsForCurrentMap);
            sessionLine.Value = StatisticsTracker.Format(statistics.CurrentSessionActiveMs);
        }

        /// <summary>A right-aligned "LABEL  value" row: a small dim caption and a brighter monospaced value.</summary>
        private partial class StatLine : CompositeDrawable
        {
            private readonly SpriteText valueText;

            public StatLine(string label)
            {
                Anchor = Anchor.TopRight;
                Origin = Anchor.TopRight;
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
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = label,
                            Colour = EditorTheme.Colours.TextFaint,
                            Font = FontUsage.Default.With(size: 10, weight: "Bold"),
                        },
                        valueText = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
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
