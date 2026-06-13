using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Edits the beatmap's own combo colours (its <c>[Colours]</c>), shown in Song Setup. These are the
    /// map's colours - distinct from the editor's palette in Settings - and are saved to the .osu. Add/remove
    /// rows; each swatch opens the HSV picker. Rebuilds itself when the colour list changes.
    /// </summary>
    public partial class MapColoursEditor : CompositeDrawable
    {
        [Resolved]
        private EditableBeatmap beatmap { get; set; } = null!;

        private FillFlowContainer flow = null!;

        // Sensible starting colours for "add" (osu! stable defaults), cycled by current count.
        private static readonly Colour4[] add_defaults =
        {
            new Colour4(255, 192, 0, 255),
            new Colour4(0, 202, 0, 255),
            new Colour4(18, 124, 255, 255),
            new Colour4(242, 24, 57, 255),
        };

        public MapColoursEditor()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = flow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
            };

            beatmap.MapColours.BindCollectionChanged((_, _) => rebuild(), true);
        }

        private void rebuild()
        {
            flow.Clear();

            if (beatmap.MapColours.Count == 0)
            {
                flow.Add(new SpriteText
                {
                    Text = "No custom colours - the default skin colours are used.",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                });
            }

            for (int i = 0; i < beatmap.MapColours.Count; i++)
            {
                var colour = beatmap.MapColours[i];
                flow.Add(new ColourRow(i + 1, colour, () => beatmap.RemoveMapColour(colour)));
            }

            flow.Add(new AddButton(() =>
                beatmap.AddMapColour(add_defaults[beatmap.MapColours.Count % add_defaults.Length])));
        }

        /// <summary>One combo-colour row: index label, swatch (opens picker) and a remove button.</summary>
        private partial class ColourRow : Container
        {
            public ColourRow(int number, Bindable<Colour4> colour, System.Action remove)
            {
                RelativeSizeAxes = Axes.X;
                Height = EditorTheme.Sizing.InputHeight;

                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = $"Combo {number}",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Body(),
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                        Children = new Drawable[]
                        {
                            new ColourSwatch(colour) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                            new RemoveButton(remove) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                        },
                    },
                };
            }
        }

        /// <summary>Small square "x" button that removes its colour row.</summary>
        private partial class RemoveButton : ClickableContainer
        {
            private Box background = null!;

            public RemoveButton(System.Action action)
            {
                Action = action;
                Size = new Vector2(EditorTheme.Sizing.InputHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "x",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Error, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }
        }

        /// <summary>"+ Add colour" button row.</summary>
        private partial class AddButton : ClickableContainer
        {
            private Box background = null!;

            public AddButton(System.Action action)
            {
                Action = action;
                RelativeSizeAxes = Axes.X;
                Height = EditorTheme.Sizing.InputHeight;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "+ Add colour",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                base.OnHoverLost(e);
            }
        }
    }
}
