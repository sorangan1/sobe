using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// The main-menu top chrome bar (full width): the app version centred, and the osu! account card flush to
    /// the top-right corner. The usage-time stats live inside the account card's drop-down; the left-side and
    /// collab/friends actions are positioned over this bar by the song-select screen.
    ///
    /// Finished like a real toolbar: a faint top-to-bottom gradient with a 1px highlight along the top edge and
    /// a hairline + soft drop shadow along the bottom, so the bar reads as a raised surface over the carousel.
    /// </summary>
    public partial class TopBar : CompositeDrawable
    {
        /// <summary>Fixed height of the bar; screens lay other chrome out below this.</summary>
        public const float HeightPx = 52;

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
                // Soft drop shadow cast just below the bar, lifting it above the carousel content.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 6,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.TopLeft,
                    Colour = ColourInfo.GradientVertical(
                        Color4.Black.Opacity(0.22f),
                        Color4.Black.Opacity(0f)),
                },
                // Body: a subtle vertical gradient (a touch lighter up top) for a brushed, raised feel.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        EditorTheme.Colours.Raised,
                        EditorTheme.Colours.Surface),
                },
                // Top highlight: a 1px light line for a crisp bevelled top edge.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Colour = Color4.White.Opacity(0.05f),
                },
                // Bottom hairline separating the bar from the content below.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = EditorTheme.Sizing.BorderThickness,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = EditorTheme.Colours.Border,
                },
                // Centred brand + version: a small accent dot, the app name, then the muted version.
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(5),
                            Colour = EditorTheme.Colours.Accent,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = AppInfo.Name,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Label(),
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = $"v{AppInfo.Version}",
                            Colour = EditorTheme.Colours.TextFaint,
                            Font = EditorTheme.Type.Label(numeric: true),
                        },
                    },
                },
                // Account card: flush to the top-right corner (no margin, full bar height).
                new UserProfileCard
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
            };
        }
    }
}
