using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Statistics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The main-menu top chrome bar (dark grey, full width): usage statistics on the left and the
    /// osu! account control (avatar + login / name + logout) on the right.
    /// </summary>
    public partial class TopBar : CompositeDrawable
    {
        /// <summary>Fixed height of the bar; screens lay other chrome out below this.</summary>
        public const float HeightPx = 52;

        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private SpriteText openValue = null!;
        private SpriteText activeValue = null!;

        public TopBar()
        {
            RelativeSizeAxes = Axes.X;
            Height = HeightPx;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = EditorTheme.Colours.Surface,
                },
                // Hairline separating the bar from the content below.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = EditorTheme.Sizing.BorderThickness,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = EditorTheme.Colours.Border,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = EditorTheme.Spacing.Xxl },
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Xxl, 0),
                    Children = new Drawable[]
                    {
                        createStat("Editor open", out openValue),
                        createStat("Mapping", out activeValue),
                    },
                },
                // App version, centred in the bar.
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = $"{AppInfo.Name} v{AppInfo.Version}",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Label(numeric: true),
                },
                new AccountWidget
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = EditorTheme.Spacing.Xl },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (statistics == null)
                return;

            openValue.Text = StatisticsTracker.Format(statistics.TotalOpenMs);
            activeValue.Text = StatisticsTracker.Format(statistics.TotalActiveMs);
        }

        private static Drawable createStat(string label, out SpriteText value)
        {
            value = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Colour = EditorTheme.Colours.Text,
                Font = EditorTheme.Type.Label(numeric: true),
            };

            return new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Label(),
                    },
                    value,
                },
            };
        }
    }
}
