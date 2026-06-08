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
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A beatmap set in the carousel. For single-difficulty sets it is the selectable entry; for
    /// multi-difficulty sets it acts as a (non-editable) header above smaller per-difficulty cards.
    /// </summary>
    public partial class BeatmapSetPanel : ClickableContainer, IHasContextMenu
    {
        public const float PANEL_HEIGHT = 72;

        private readonly BeatmapSetModel model;
        private readonly bool isNew;
        private readonly bool isHeader;
        private readonly LargeTextureStore? textures;

        private Box background = null!;
        private Box newAccentBar = null!;
        private Container selectionOutline = null!;

        private bool selected;

        /// <summary>Invoked by the "Edit" context-menu item (only set for editable, single-difficulty cards).</summary>
        public Action? EditAction;

        public MenuItem[] ContextMenuItems => EditAction == null
            ? Array.Empty<MenuItem>()
            : new MenuItem[] { new MenuItem("Edit", EditAction) };

        public BeatmapSetPanel(BeatmapSetModel model, bool isNew = false, bool isHeader = false, LargeTextureStore? textures = null)
        {
            this.model = model;
            this.isNew = isNew;
            this.isHeader = isHeader;
            this.textures = textures;

            RelativeSizeAxes = Axes.X;
            Height = PANEL_HEIGHT;
            Masking = true;
            CornerRadius = 6;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            int diffCount = model.Difficulties.Count;
            string author = string.IsNullOrEmpty(model.Author) ? "unknown" : model.Author;
            string subtitle = isHeader
                ? $"mapped by {author}   -   {diffCount} difficulties"
                : $"mapped by {author}   -   {model.MaxStarRating:0.00}*";

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = isNew ? OsuColour.BackgroundRaised : OsuColour.Surface,
                },
                CarouselBackground.TryCreate(textures, model, model.BackgroundFile) ?? Empty(),
                newAccentBar = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 6,
                    Colour = OsuColour.Purple,
                    Alpha = isNew ? 1 : 0,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Left = 18, Right = isNew ? 78 : 18, Vertical = 10 },
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = $"{model.Artist} - {model.Title}",
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 22, weight: "SemiBold"),
                            Truncate = true,
                            RelativeSizeAxes = Axes.X,
                        },
                        new SpriteText
                        {
                            Text = subtitle,
                            Colour = OsuColour.TextMuted,
                            Font = FontUsage.Default.With(size: 15),
                        },
                    },
                },
                newBadge(),
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

        /// <summary>A small "NEW" pill shown on freshly-created, not-yet-opened maps.</summary>
        private Drawable newBadge() => new Container
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Margin = new MarginPadding { Right = 16 },
            Size = new Vector2(52, 24),
            Alpha = isNew ? 1 : 0,
            Masking = true,
            CornerRadius = 12,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Purple,
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "NEW",
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 13, weight: "Bold"),
                },
            },
        };

        /// <summary>Toggles the yellow selection border.</summary>
        public void SetSelected(bool value)
        {
            if (selected == value)
                return;

            selected = value;
            selectionOutline.FadeTo(value ? 1 : 0, 150, Easing.OutQuint);
            background.FadeColour(value ? OsuColour.BackgroundRaised : (isNew ? OsuColour.BackgroundRaised : OsuColour.Surface), 150, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!selected)
                background.FadeColour(OsuColour.BackgroundRaised, 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!selected)
                background.FadeColour(isNew ? OsuColour.BackgroundRaised : OsuColour.Surface, 150, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
