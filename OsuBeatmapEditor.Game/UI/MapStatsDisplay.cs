using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Screens.Edit;
using OsuBeatmapEditor.Game.Statistics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// In-editor readout for the current map, bottom-right above the FPS counter: the map's headline difficulty
    /// stats (CS / AR, info only) on top, then the editing time - total across all sessions and this session.
    /// Styled to match the FPS counter (rounded translucent panel, muted label + brighter value).
    /// </summary>
    public partial class MapStatsDisplay : CompositeDrawable
    {
        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        // The map's editable difficulty (cached by the editor). Absent under the test browser, so null-guarded.
        [Resolved(CanBeNull = true)]
        private EditableBeatmap? editable { get; set; }

        private StatLine? csLine;
        private StatLine? arLine;
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
            var rows = new System.Collections.Generic.List<Drawable>();

            // Headline map stats (read-only). CS/AR sit above the editing-time lines; an extra gap below AR
            // separates the difficulty group from the time group (a relative-width rule can't live in an
            // auto-sizing flow, so the spacing carries the separation instead).
            if (editable != null)
            {
                csLine = new StatLine("CS");
                arLine = new StatLine("AR") { Margin = new MarginPadding { Bottom = 4 } };
                rows.Add(csLine);
                rows.Add(arLine);

                editable.Cs.BindValueChanged(v => csLine.Value = format(v.NewValue), true);
                editable.Ar.BindValueChanged(v => arLine.Value = format(v.NewValue), true);
            }

            rows.Add(totalLine = new StatLine("TOTAL"));
            rows.Add(sessionLine = new StatLine("SESSION"));

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark, Alpha = 0.6f },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 3),
                    Padding = new MarginPadding { Horizontal = 8, Vertical = 5 },
                    Children = rows,
                },
            };
        }

        /// <summary>Difficulty value with at most one decimal (drops a trailing ".0"): 4 -> "4", 9.3 -> "9.3".</summary>
        private static string format(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        protected override void Update()
        {
            base.Update();

            // Show the card if there's anything to show (CS/AR alone is enough, even without the stats tracker).
            if (statistics == null && editable == null)
            {
                Alpha = 0;
                return;
            }

            Alpha = 1;

            if (statistics != null)
            {
                totalLine.Value = StatisticsTracker.Format(statistics.ActiveMsForCurrentMap);
                sessionLine.Value = StatisticsTracker.Format(statistics.CurrentSessionActiveMs);
            }
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
