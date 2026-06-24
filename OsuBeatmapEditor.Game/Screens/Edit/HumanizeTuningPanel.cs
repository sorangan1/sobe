using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Extensions.Color4Extensions;
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
    /// A live tuning panel for the "Humanize" Auto cursor: a scrollable list of sliders, each bound straight to a
    /// <see cref="HumanizeTuning"/> field, so dragging one changes the AU cursor's motion in real time (the cursor
    /// reads the fields every frame). Opened from the AU chip's mini-menu. "Reset" restores the calibrated defaults.
    /// </summary>
    public partial class HumanizeTuningPanel : VisibilityContainer
    {
        private readonly Action onClose;
        private readonly Action onSave;
        private readonly List<(BindableFloat bindable, Func<float> get)> rows = new();
        private SpriteText savedFlash = null!;

        public HumanizeTuningPanel(Action onClose, Action onSave)
        {
            this.onClose = onClose;
            this.onSave = onSave;
            Width = 312;
            Height = 580;
        }

        protected override void PopIn() => this.FadeIn(120, Easing.OutQuad);
        protected override void PopOut() => this.FadeOut(120, Easing.OutQuad);

        [BackgroundDependencyLoader]
        private void load()
        {
            var list = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                Padding = new MarginPadding(EditorTheme.Spacing.Md),
            };

            group(list, "STACK");
            row(list, "Stack range lo", 0, 40, () => HumanizeTuning.StackLo, v => HumanizeTuning.StackLo = v);
            row(list, "Stack range hi", 0, 60, () => HumanizeTuning.StackHi, v => HumanizeTuning.StackHi = v);
            row(list, "Stack hold start", 0, 1, () => HumanizeTuning.StackHoldStart, v => HumanizeTuning.StackHoldStart = v);

            group(list, "FLOW (stream vs jump)");
            row(list, "Spatial flow lo", 0, 150, () => HumanizeTuning.SpatialFlowLo, v => HumanizeTuning.SpatialFlowLo = v);
            row(list, "Spatial flow hi", 50, 300, () => HumanizeTuning.SpatialFlowHi, v => HumanizeTuning.SpatialFlowHi = v);
            row(list, "Stream gap fast (ms)", 40, 200, () => HumanizeTuning.StreamGapFastMs, v => HumanizeTuning.StreamGapFastMs = v);
            row(list, "Stream gap slow (ms)", 60, 320, () => HumanizeTuning.StreamGapSlowMs, v => HumanizeTuning.StreamGapSlowMs = v);
            row(list, "Stream regular lo", 0, 1, () => HumanizeTuning.StreamRegularLo, v => HumanizeTuning.StreamRegularLo = v);
            row(list, "Stream regular hi", 0, 1, () => HumanizeTuning.StreamRegularHi, v => HumanizeTuning.StreamRegularHi = v);

            group(list, "OVERSHOOT");
            row(list, "On lo", 0, 400, () => HumanizeTuning.OvershootOnLo, v => HumanizeTuning.OvershootOnLo = v);
            row(list, "On hi", 0, 400, () => HumanizeTuning.OvershootOnHi, v => HumanizeTuning.OvershootOnHi = v);
            row(list, "Off lo", 100, 800, () => HumanizeTuning.OvershootOffLo, v => HumanizeTuning.OvershootOffLo = v);
            row(list, "Off hi", 200, 1200, () => HumanizeTuning.OvershootOffHi, v => HumanizeTuning.OvershootOffHi = v);
            row(list, "Amount", 0, 3, () => HumanizeTuning.OvershootAmount, v => HumanizeTuning.OvershootAmount = v);
            row(list, "Reversal gate", 0, 1, () => HumanizeTuning.OvershootReversalGate, v => HumanizeTuning.OvershootReversalGate = v);

            group(list, "ARC (bow on jumps)");
            row(list, "Turn threshold", 0, 1, () => HumanizeTuning.ArcTurnThreshold, v => HumanizeTuning.ArcTurnThreshold = v);
            row(list, "Outside amount", 0, 0.5f, () => HumanizeTuning.ArcOutsideAmount, v => HumanizeTuning.ArcOutsideAmount = v);
            row(list, "Figure-8 amount", 0, 0.5f, () => HumanizeTuning.ArcFigure8Amount, v => HumanizeTuning.ArcFigure8Amount = v);
            row(list, "Max bow (px)", 50, 500, () => HumanizeTuning.ArcMaxPx, v => HumanizeTuning.ArcMaxPx = v);

            group(list, "AIM / JITTER");
            row(list, "Aim error", 0, 1.5f, () => HumanizeTuning.AimErrorAmount, v => HumanizeTuning.AimErrorAmount = v);
            row(list, "Jitter amount", 0, 4, () => HumanizeTuning.JitterAmount, v => HumanizeTuning.JitterAmount = v);
            row(list, "Jitter steady damp", 0, 1, () => HumanizeTuning.JitterSteadyDamp, v => HumanizeTuning.JitterSteadyDamp = v);

            group(list, "SLIDERS");
            row(list, "Release frac", 0, 0.3f, () => HumanizeTuning.SliderReleaseFrac, v => HumanizeTuning.SliderReleaseFrac = v);
            row(list, "Release max (ms)", 0, 100, () => HumanizeTuning.SliderReleaseMaxMs, v => HumanizeTuning.SliderReleaseMaxMs = v);
            row(list, "Laziness", 0, 1, () => HumanizeTuning.SliderLaziness, v => HumanizeTuning.SliderLaziness = v);

            group(list, "LONG-PAUSE DRIFT");
            row(list, "Slow lo (ms)", 0, 1000, () => HumanizeTuning.CentreDriftSlowLo, v => HumanizeTuning.CentreDriftSlowLo = v);
            row(list, "Slow hi (ms)", 100, 2000, () => HumanizeTuning.CentreDriftSlowHi, v => HumanizeTuning.CentreDriftSlowHi = v);
            row(list, "Amount", 0, 1, () => HumanizeTuning.CentreDriftAmount, v => HumanizeTuning.CentreDriftAmount = v);

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.4f),
                    Radius = 12,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            header(),
                            new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorTheme.Colours.Sunken },
                            new BasicScrollContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 580 - 40 - 1, // panel height minus the header + divider (absolute px)
                                ScrollbarVisible = true,
                                Child = list,
                            },
                        },
                    },
                },
            };

            refresh();
        }

        private Drawable header() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Md },
            Children = new Drawable[]
            {
                new FillFlowContainer
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
                            Text = "HUMANIZE TUNING",
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Caption(),
                        },
                        savedFlash = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "saved",
                            Alpha = 0,
                            Colour = EditorTheme.Colours.Velocity,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
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
                        new TextButton("Save", () => { onSave(); savedFlash.FadeIn(60).Then().FadeOut(900, Easing.InQuad); }),
                        new TextButton("Reset", () => { HumanizeTuning.ResetToDefaults(); refresh(); }),
                        new TextButton("X", onClose),
                    },
                },
            },
        };

        private static void group(FillFlowContainer list, string title) => list.Add(new SpriteText
        {
            Text = title,
            Colour = EditorTheme.Colours.Accent,
            Font = EditorTheme.Type.Caption(),
            Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm, Bottom = 2 },
        });

        private void row(FillFlowContainer list, string label, float min, float max, Func<float> get, Action<float> set)
        {
            var bindable = new BindableFloat(get())
            {
                MinValue = min,
                MaxValue = max,
                Precision = (max - min) / 250f,
            };

            var value = new SpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(numeric: true),
            };

            bindable.BindValueChanged(v =>
            {
                set(v.NewValue);
                value.Text = v.NewValue.ToString(max <= 3f ? "0.00" : "0");
            }, true);

            rows.Add((bindable, get));

            list.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Margin = new MarginPadding { Bottom = 3 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 14,
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = label,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Label(),
                            },
                            value,
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 9,
                        Child = new BasicSliderBar<float>
                        {
                            RelativeSizeAxes = Axes.Both,
                            Current = bindable,
                            BackgroundColour = EditorTheme.Colours.Sunken,
                            SelectionColour = EditorTheme.Colours.Selection,
                        },
                    },
                },
            });
        }

        /// <summary>Pulls every slider back in line with the current field values (after a Reset).</summary>
        private void refresh()
        {
            foreach (var (bindable, get) in rows)
                bindable.Value = get();
        }

        // Eat clicks/scrolls so interacting with the panel never falls through to the editor behind it.
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnScroll(ScrollEvent e) => true;
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        /// <summary>A small text button (Reset / close).</summary>
        private partial class TextButton : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Box bg;

            public TextButton(string text, Action onClick)
            {
                this.onClick = onClick;
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;
                InternalChildren = new Drawable[]
                {
                    bg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                    new SpriteText
                    {
                        Text = text,
                        Margin = new MarginPadding { Horizontal = 8, Vertical = 4 },
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e) { bg.FadeColour(EditorTheme.Colours.Selection, 80); return true; }
            protected override void OnHoverLost(HoverLostEvent e) => bg.FadeColour(EditorTheme.Colours.Sunken, 80);

            protected override bool OnClick(ClickEvent e) { onClick(); return true; }
        }
    }
}
