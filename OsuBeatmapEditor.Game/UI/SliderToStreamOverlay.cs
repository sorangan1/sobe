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
        private StreamCountBox countBox = null!;
        private AccelBox curveBox = null!;
        private SpriteText curveCaption = null!;

        // The true circle count (hard limits). The bar only spans a slider-sized sub-range (countBar); to go
        // beyond it you type the number into countBox, which writes straight to count.
        private readonly BindableInt count = new BindableInt(8) { MinValue = 2, MaxValue = 256 };
        private readonly BindableInt countBar = new BindableInt(8) { MinValue = 2, MaxValue = 32 };
        private bool syncingCount;

        // Acceleration intensity as a fraction: 0 = even, ±1 = the bar's ends (ratio 5^1 = 5x last/first gap; see
        // EditorScreen.streamSpacing). The bar only spans ±1; to push harder - useful for dense streams, where 5x
        // spread across many gaps reads as weak - you type a larger percentage into curveBox, which writes
        // straight to curve (hard cap ±3 = 125x). curveBar is the ±1 proxy the drag bar binds to.
        private readonly BindableFloat curve = new BindableFloat(0) { MinValue = -3f, MaxValue = 3f, Precision = 0.02f };
        private readonly BindableFloat curveBar = new BindableFloat(0) { MinValue = -1f, MaxValue = 1f, Precision = 0.02f };
        private bool syncingCurve;

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
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 26,
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Text = "Circles",
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Colour = EditorTheme.Colours.TextMuted,
                                                Font = EditorTheme.Type.Body(),
                                            },
                                            countBox = new StreamCountBox(count)
                                            {
                                                Anchor = Anchor.CentreRight,
                                                Origin = Anchor.CentreRight,
                                                Size = new Vector2(58, 24),
                                            },
                                        },
                                    },
                                    sliderRow(new BasicSliderBar<int>
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Current = countBar,
                                        BackgroundColour = EditorTheme.Colours.Sunken,
                                        SelectionColour = EditorTheme.Colours.Selection,
                                    }),
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 26,
                                        Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Text = "Acceleration",
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Colour = EditorTheme.Colours.TextMuted,
                                                Font = EditorTheme.Type.Body(),
                                            },
                                            curveBox = new AccelBox(curve)
                                            {
                                                Anchor = Anchor.CentreRight,
                                                Origin = Anchor.CentreRight,
                                                Size = new Vector2(58, 24),
                                            },
                                        },
                                    },
                                    sliderRow(new BasicSliderBar<float>
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Current = curveBar,
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

            // Two-way sync between the true count and the (slider-sized) bar proxy, guarded against feedback.
            // The number box binds straight to `count`, so typing a value beyond the bar's range just works.
            count.BindValueChanged(c =>
            {
                if (!syncingCount)
                {
                    syncingCount = true;
                    countBar.Value = Math.Clamp(c.NewValue, countBar.MinValue, countBar.MaxValue);
                    syncingCount = false;
                }
                firePreview();
            }, true);
            countBar.BindValueChanged(c =>
            {
                if (syncingCount)
                    return;
                syncingCount = true;
                count.Value = c.NewValue; // fires count's handler, which previews
                syncingCount = false;
            });
            // Same two-way sync for acceleration: the ±1 bar proxy (curveBar) drives the drag bar, while the box
            // types straight into `curve` and can push past ±1.
            curve.BindValueChanged(c =>
            {
                if (!syncingCurve)
                {
                    syncingCurve = true;
                    curveBar.Value = Math.Clamp(c.NewValue, curveBar.MinValue, curveBar.MaxValue);
                    syncingCurve = false;
                }
                curveCaption.Text = captionFor(c.NewValue);
                firePreview();
            }, true);
            curveBar.BindValueChanged(c =>
            {
                if (syncingCurve)
                    return;
                syncingCurve = true;
                curve.Value = c.NewValue;
                syncingCurve = false;
            });
        }

        /// <summary>
        /// Opens the panel, seeding the circle count with the beat-snap default and resetting the curve.
        /// <paramref name="barMax"/> caps the drag bar to the slider's own capacity (a comfortable range); larger
        /// counts are still reachable by typing into the number box.
        /// </summary>
        public void Show(int defaultCount, int barMax)
        {
            committing = false;

            syncingCount = true;
            countBar.MaxValue = Math.Max(barMax, countBar.MinValue + 1);
            count.Value = Math.Clamp(defaultCount, count.MinValue, count.MaxValue);
            countBar.Value = Math.Clamp(count.Value, countBar.MinValue, countBar.MaxValue);
            syncingCount = false;

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

        private static string captionFor(float v) =>
            Math.Abs(v) < 0.025f ? "evenly spaced"
            : v > 0 ? "accelerates - packed at the start"
            : "decelerates - packed at the end";

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

        /// <summary>
        /// A compact numeric box for the circle count, bound to a <see cref="BindableInt"/>. Digits only; commits
        /// on Enter / focus loss, clamping to the bindable's (hard) range, and reflects external changes (the drag
        /// bar). Lets the mapper type an exact count past the drag bar's slider-sized cap.
        /// </summary>
        private partial class StreamCountBox : BasicTextBox
        {
            private readonly BindableInt bindable;

            public StreamCountBox(BindableInt bindable)
            {
                this.bindable = bindable;
                CommitOnFocusLost = true;
                LengthLimit = 3;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                updateText();
                bindable.BindValueChanged(_ => updateText());
            }

            protected override bool CanAddCharacter(char character) => char.IsDigit(character);

            private void updateText() => Text = bindable.Value.ToString();

            protected override void Commit()
            {
                if (int.TryParse(Text, out int value))
                    bindable.Value = Math.Clamp(value, bindable.MinValue, bindable.MaxValue);

                updateText();
                base.Commit();
            }
        }

        /// <summary>
        /// A compact box for the acceleration intensity, shown and edited as a percentage (e.g. "60%", "-40%")
        /// that maps to the underlying curve fraction (percent / 100). Digits and a leading minus; commits on
        /// Enter / focus loss, clamping to the bindable's hard range. Lets the mapper type past the drag bar's
        /// ±100% for stronger bunching on dense streams.
        /// </summary>
        private partial class AccelBox : BasicTextBox
        {
            private readonly BindableFloat bindable;

            public AccelBox(BindableFloat bindable)
            {
                this.bindable = bindable;
                CommitOnFocusLost = true;
                LengthLimit = 5;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                updateText();
                bindable.BindValueChanged(_ => updateText());
            }

            protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '-';

            private void updateText() => Text = $"{bindable.Value * 100:0}%";

            protected override void Commit()
            {
                string digits = Text.Replace("%", string.Empty).Trim();
                if (int.TryParse(digits, out int percent))
                    bindable.Value = Math.Clamp(percent / 100f, bindable.MinValue, bindable.MaxValue);

                updateText();
                base.Commit();
            }
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
