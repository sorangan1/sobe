using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A small, selectable card representing a single difficulty within a multi-difficulty set.
    /// </summary>
    public partial class BeatmapDiffPanel : ClickableContainer, ICarouselPanel, IHasContextMenu
    {
        public const float PANEL_HEIGHT = 44;

        /// <summary>Right-click menu entries; set by the carousel when the card is realised.</summary>
        public MenuItem[] ContextMenuItems { get; set; } = Array.Empty<MenuItem>();

        private readonly BeatmapSetModel set;
        private readonly BeatmapDifficultyModel difficulty;
        private readonly LargeTextureStore? textures;

        private Box background = null!;
        private Container selectionOutline = null!;

        private bool selected;

        public BeatmapDiffPanel(BeatmapSetModel set, BeatmapDifficultyModel difficulty, LargeTextureStore? textures = null)
        {
            this.set = set;
            this.difficulty = difficulty;
            this.textures = textures;

            RelativeSizeAxes = Axes.X;
            Height = PANEL_HEIGHT;
            Masking = true;
            CornerRadius = 6;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.BackgroundRaised,
                },
                CarouselBackground.TryCreate(textures, set, difficulty.BackgroundFile) ?? Empty(),
                // A short tint band on the right edge in this difficulty's star-rating colour (fading in from
                // transparent), leaving the left - where the star pill and name sit - untinted.
                new Box
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.32f,
                    Colour = ColourInfo.GradientHorizontal(
                        difficultyColour(0f),
                        difficultyColour(0.7f)),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.X,
                    Height = PANEL_HEIGHT,
                    Margin = new MarginPadding { Left = 22 },
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        // Star rating first, then the difficulty name.
                        new StarRatingDisplay(difficulty.StarRating)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = difficulty.DifficultyName,
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 16, weight: "SemiBold"),
                        },
                    },
                },
                selectionOutline = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 6,
                    BorderThickness = 3,
                    BorderColour = OsuColour.Yellow,
                    Alpha = 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Yellow, Alpha = 0, AlwaysPresent = true },
                },
            };
        }

        /// <summary>How far a selected card slides left to read as selected (cards are right-anchored).</summary>
        private const float selected_shift = 14;

        /// <summary>This difficulty's star-rating colour at the given alpha.</summary>
        private Color4 difficultyColour(float alpha)
        {
            var c = StarRatingColour.For(difficulty.StarRating);
            return new Color4(c.R, c.G, c.B, alpha);
        }

        public void SetSelected(bool value)
        {
            if (selected == value)
                return;

            selected = value;
            selectionOutline.FadeTo(value ? 1 : 0, 150, Easing.OutQuint);
            background.FadeColour(value ? OsuColour.Surface : OsuColour.BackgroundRaised, 150, Easing.OutQuint);
            this.MoveToX(value ? -selected_shift : 0, 200, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!selected)
                background.FadeColour(OsuColour.Surface, 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!selected)
                background.FadeColour(OsuColour.BackgroundRaised, 150, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
