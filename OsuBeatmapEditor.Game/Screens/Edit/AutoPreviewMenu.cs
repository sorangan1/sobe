using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The little settings popover that drops below the "AU" Auto-preview chip: pick the cursor colour from a
    /// row of swatches and set the trail length with a slider. Two-way bound to the editor's Auto state.
    /// </summary>
    public partial class AutoPreviewMenu : CompositeDrawable
    {
        private readonly Bindable<Color4> colour;
        private readonly BindableInt trailLength;
        private readonly BindableFloat trailWidth;
        private readonly BindableBool keyOverlay;

        // The colours offered for the cursor. Yellow first (the default the user asked for).
        private static readonly Color4[] swatch_colours =
        {
            new Color4(1f, 0.86f, 0.2f, 1f),   // yellow
            Color4.White,
            EditorTheme.Colours.Accent,        // pink
            EditorTheme.Colours.Error,         // red
            EditorTheme.Colours.Velocity,      // green
            EditorTheme.Colours.Info,          // blue
        };

        private readonly List<SwatchButton> swatches = new List<SwatchButton>();

        public AutoPreviewMenu(Bindable<Color4> colour, BindableInt trailLength, BindableFloat trailWidth, BindableBool keyOverlay)
        {
            this.colour = colour;
            this.trailLength = trailLength;
            this.trailWidth = trailWidth;
            this.keyOverlay = keyOverlay;

            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var swatchFlow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
            };

            foreach (var c in swatch_colours)
            {
                var swatch = new SwatchButton(c, () => colour.Value = c);
                swatches.Add(swatch);
                swatchFlow.Add(swatch);
            }

            var trailValue = valueText();
            var widthValue = valueText();

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = EditorTheme.Colours.Surface,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                        Padding = new MarginPadding(EditorTheme.Spacing.Md),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "AUTO CURSOR",
                                Colour = EditorTheme.Colours.TextMuted,
                                Font = EditorTheme.Type.Caption(),
                            },
                            label("Colour"),
                            swatchFlow,
                            headerRow("Trail", trailValue),
                            new Container
                            {
                                Width = 168,
                                Height = 10,
                                Child = new BasicSliderBar<int>
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Current = trailLength,
                                    BackgroundColour = EditorTheme.Colours.Sunken,
                                    SelectionColour = EditorTheme.Colours.Selection,
                                },
                            },
                            headerRow("Width", widthValue),
                            new Container
                            {
                                Width = 168,
                                Height = 10,
                                Child = new BasicSliderBar<float>
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Current = trailWidth,
                                    BackgroundColour = EditorTheme.Colours.Sunken,
                                    SelectionColour = EditorTheme.Colours.Selection,
                                },
                            },
                            new ToggleRow("Key overlay", keyOverlay),
                        },
                    },
                },
            };

            colour.BindValueChanged(c => updateSwatches(c.NewValue), true);
            trailLength.BindValueChanged(t => trailValue.Text = t.NewValue.ToString(), true);
            trailWidth.BindValueChanged(w => widthValue.Text = $"{w.NewValue:0.0}x", true);
        }

        private static SpriteText valueText() => new SpriteText
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Caption(numeric: true),
        };

        private static Container headerRow(string text, SpriteText value) => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 16,
            Children = new Drawable[] { label(text), value },
        };

        private void updateSwatches(Color4 selected)
        {
            foreach (var s in swatches)
                s.SetSelected(s.SwatchColour.Equals(selected));
        }

        private static SpriteText label(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.Text,
            Font = EditorTheme.Type.Label(),
        };

        /// <summary>A labelled on/off switch bound to a <see cref="BindableBool"/> (click anywhere on the row toggles it).</summary>
        private partial class ToggleRow : CompositeDrawable
        {
            private readonly BindableBool current;
            private readonly Container knob;
            private readonly Box track;

            public ToggleRow(string text, BindableBool current)
            {
                this.current = current;

                RelativeSizeAxes = Axes.X;
                Height = 20;

                InternalChildren = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = text,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Size = new Vector2(34, 18),
                        Masking = true,
                        CornerRadius = 9,
                        Children = new Drawable[]
                        {
                            track = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                            knob = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(14),
                                Position = new Vector2(2, 0),
                                Masking = true,
                                CornerRadius = 7,
                                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
                            },
                        },
                    },
                };
            }

            [BackgroundDependencyLoader]
            private void load() => current.BindValueChanged(v => updateState(v.NewValue), true);

            private void updateState(bool on)
            {
                track.FadeColour(on ? EditorTheme.Colours.Info : EditorTheme.Colours.Sunken, 90);
                knob.MoveToX(on ? 18 : 2, 90, Easing.OutQuad);
            }

            protected override bool OnClick(ClickEvent e)
            {
                current.Value = !current.Value;
                return true;
            }
        }

        /// <summary>A small clickable colour swatch; lights a white ring when it is the active colour.</summary>
        private partial class SwatchButton : CircularContainer
        {
            public readonly Color4 SwatchColour;
            private readonly System.Action onClick;

            public SwatchButton(Color4 colour, System.Action onClick)
            {
                SwatchColour = colour;
                this.onClick = onClick;

                Size = new Vector2(20);
                Masking = true;
                BorderThickness = 0;
                BorderColour = Color4.White;
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colour };
            }

            public void SetSelected(bool selected) => BorderThickness = selected ? 3 : 0;

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }
    }
}
