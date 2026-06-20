using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Screens.Edit;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Non-intrusive, draggable panel shown when converting a slider into a stream. Lets the mapper pick how
    /// many circles the stream will have (seeded with the beat-snap tick count) and a spacing curve that ramps
    /// the gaps so the circles pack toward the start or the end (acceleration / deceleration). The result is
    /// previewed live on the playfield as the sliders move - there is no dim, and the panel can be dragged out
    /// of the way by its header.
    /// </summary>
    public partial class SliderToStreamOverlay : VisibilityContainer
    {
        /// <summary>Fired continuously as the count / curve change, so the editor can redraw the live preview.</summary>
        public Action<int, float>? Preview;

        /// <summary>Fired when the mapper commits (Convert button / Enter).</summary>
        public Action<int, float>? Confirmed;

        /// <summary>Fired when the panel is dismissed without committing (Cancel button / Escape / close).</summary>
        public Action? Cancelled;

        private Container panel = null!;
        private SpriteText countValue = null!;
        private SpriteText curveValue = null!;
        private SpriteText curveCaption = null!;

        private readonly BindableInt count = new BindableInt(8) { MinValue = 2, MaxValue = 128 };
        private readonly BindableFloat curve = new BindableFloat(0) { MinValue = -3f, MaxValue = 3f, Precision = 0.05f };

        private bool committing;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            // The root spans the screen so the panel can be dragged anywhere, but it does NOT capture input -
            // there's no dim box and no click-catcher, so the editor underneath stays fully interactive.
            RelativeSizeAxes = Axes.Both;

            InternalChild = panel = new DraggablePanel
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-EditorTheme.Spacing.Xxl, 96),
                Width = 320,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = new osuTK.Graphics.Color4(0f, 0f, 0f, 0.5f),
                    Radius = 12f,
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = EditorTheme.Colours.Raised,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new DragHandle(d => panel.Position += d)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 34,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = EditorTheme.Colours.Surface,
                                    },
                                    new SpriteText
                                    {
                                        Text = "Slider to stream",
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Lg },
                                        Colour = EditorTheme.Colours.Accent,
                                        Font = EditorTheme.Type.Body(),
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding(EditorTheme.Spacing.Lg),
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                                Children = new Drawable[]
                                {
                                    headerRow("Circles", countValue = valueText()),
                                    sliderRow(new BasicSliderBar<int>
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Current = count,
                                        BackgroundColour = EditorTheme.Colours.Sunken,
                                        SelectionColour = EditorTheme.Colours.Selection,
                                    }),
                                    headerRow("Spacing curve", curveValue = valueText()),
                                    sliderRow(new BasicSliderBar<float>
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Current = curve,
                                        BackgroundColour = EditorTheme.Colours.Sunken,
                                        SelectionColour = EditorTheme.Colours.Selection,
                                    }),
                                    curveCaption = new SpriteText
                                    {
                                        Colour = EditorTheme.Colours.TextMuted,
                                        Font = EditorTheme.Type.Caption(),
                                    },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Horizontal,
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Md },
                                        Children = new Drawable[]
                                        {
                                            new OsuButton("Cancel", OsuColour.Surface)
                                            {
                                                Size = new Vector2(96, 36),
                                                Action = () => Hide(),
                                            },
                                            new OsuButton("Convert", OsuColour.Pink)
                                            {
                                                Size = new Vector2(108, 36),
                                                Action = Commit,
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            count.BindValueChanged(c => { countValue.Text = c.NewValue.ToString(); firePreview(); }, true);
            curve.BindValueChanged(c =>
            {
                curveValue.Text = describeCurve(c.NewValue);
                curveCaption.Text = captionFor(c.NewValue);
                firePreview();
            }, true);
        }

        /// <summary>Opens the panel, seeding the circle count with the beat-snap default and resetting the curve.</summary>
        public void Show(int defaultCount)
        {
            committing = false;
            count.Value = Math.Clamp(defaultCount, count.MinValue, count.MaxValue);
            curve.Value = 0;
            Show();
            firePreview();
        }

        /// <summary>Commits the conversion at the current values (Convert button / Enter).</summary>
        public void Commit()
        {
            committing = true;
            Confirmed?.Invoke(count.Value, curve.Value);
            Hide();
        }

        private void firePreview()
        {
            if (State.Value == Visibility.Visible)
                Preview?.Invoke(count.Value, curve.Value);
        }

        private static string describeCurve(float v) =>
            Math.Abs(v) < 0.025f ? "even" : (v > 0 ? $"+{v:0.00}" : $"{v:0.00}");

        private static string captionFor(float v) =>
            Math.Abs(v) < 0.025f ? "evenly spaced"
            : v > 0 ? "accelerates - packed at the start"
            : "decelerates - packed at the end";

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
            Height = 18,
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = text,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
                value,
            },
        };

        private static Container sliderRow(Drawable slider) => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 12,
            Child = slider,
        };

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.98f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

            // Dismissed without committing -> tell the editor to drop the live preview and restore the slider.
            if (!committing)
                Cancelled?.Invoke();
            committing = false;
        }

        /// <summary>A floating panel that blocks input within its own bounds (so clicks don't leak to the playfield).</summary>
        private partial class DraggablePanel : Container
        {
            protected override bool OnMouseDown(MouseDownEvent e) => true;
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;
            protected override bool OnScroll(ScrollEvent e) => true;
        }

        /// <summary>A header strip that drags its owning panel around by reporting pointer deltas.</summary>
        private partial class DragHandle : Container
        {
            private readonly Action<Vector2> onDrag;

            public DragHandle(Action<Vector2> onDrag)
            {
                this.onDrag = onDrag;
            }

            protected override bool OnDragStart(DragStartEvent e) => true;
            protected override void OnDrag(DragEvent e) => onDrag(e.Delta);
        }
    }
}
