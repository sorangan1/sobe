using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
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

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A small, selectable card representing a single difficulty within a multi-difficulty set.
    /// </summary>
    public partial class BeatmapDiffPanel : ClickableContainer, IHasContextMenu
    {
        public const float PANEL_HEIGHT = 44;

        private readonly BeatmapSetModel set;
        private readonly BeatmapDifficultyModel difficulty;
        private readonly LargeTextureStore? textures;

        private Box background = null!;
        private Container selectionOutline = null!;

        private bool selected;

        /// <summary>Invoked by the "Edit" context-menu item.</summary>
        public Action? EditAction;

        public MenuItem[] ContextMenuItems => EditAction == null
            ? Array.Empty<MenuItem>()
            : new MenuItem[] { new MenuItem("Edit", EditAction) };

        public BeatmapDiffPanel(BeatmapSetModel set, BeatmapDifficultyModel difficulty, LargeTextureStore? textures = null)
        {
            this.set = set;
            this.difficulty = difficulty;
            this.textures = textures;

            RelativeSizeAxes = Axes.X;
            Height = PANEL_HEIGHT;
            Masking = true;
            CornerRadius = 5;
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
                    BorderThickness = 3,
                    BorderColour = OsuColour.Yellow,
                    Alpha = 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Yellow, Alpha = 0, AlwaysPresent = true },
                },
            };
        }

        public void SetSelected(bool value)
        {
            if (selected == value)
                return;

            selected = value;
            selectionOutline.FadeTo(value ? 1 : 0, 150, Easing.OutQuint);
            background.FadeColour(value ? OsuColour.Surface : OsuColour.BackgroundRaised, 150, Easing.OutQuint);
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
