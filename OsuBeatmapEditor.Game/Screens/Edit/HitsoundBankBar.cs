using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A compact horizontal bank bar shown while the hitsound-lanes editor is open (mirrors osu!lazer's hitsound
    /// toolbar): a Normal-bank selector and an Addition-bank selector, each Auto / Normal / Soft / Drum. Applies
    /// to the current selection (or the pending defaults when nothing is selected), polled each frame.
    /// </summary>
    public partial class HitsoundBankBar : CompositeDrawable
    {
        public Func<EditorScreen.HitsoundState>? StateProvider;
        public Action<SampleBank>? SetNormalBank;
        public Action<SampleBank>? SetAdditionBank;
        public Action<float>? SetVolume;
        public Action<int>? SetIndex;
        public Action? CopyHitsounds;
        public Action? PasteHitsounds;
        public Func<bool>? HasClip;

        // Volume slider with a "push state -> slider only when it actually changed" guard, so polling never fights a drag.
        private readonly BindableFloat volume = new BindableFloat { MinValue = 0f, MaxValue = 1f, Precision = 0.01f };
        private float lastVolume = -1f;
        private bool suppressVolume;
        private SpriteText volumeReadout = null!;
        private SpriteText indexReadout = null!;
        private int currentIndex;
        private TextButton pasteButton = null!;

        private static readonly (SampleBank Bank, string Label)[] bank_options =
        {
            (SampleBank.Auto, "A"),
            (SampleBank.Normal, "N"),
            (SampleBank.Soft, "S"),
            (SampleBank.Drum, "D"),
        };

        private readonly Dictionary<SampleBank, Chip> normalChips = new Dictionary<SampleBank, Chip>();
        private readonly Dictionary<SampleBank, Chip> additionChips = new Dictionary<SampleBank, Chip>();

        public HitsoundBankBar()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Compact, transparent layout: it lives inside the timeline's lane gutter (which provides the background),
            // so hitsounding needs no separate side panel. Rows: Normal bank, Addition bank, Volume, Index, Copy/Paste.
            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
                Children = new Drawable[]
                {
                    group("N", normalChips, b => SetNormalBank?.Invoke(b)),
                    group("A", additionChips, b => SetAdditionBank?.Invoke(b)),
                    volumeRow(),
                    indexRow(),
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                        Children = new Drawable[]
                        {
                            new TextButton("Copy", () => CopyHitsounds?.Invoke(), 72),
                            pasteButton = new TextButton("Paste", () => PasteHitsounds?.Invoke(), 72),
                        },
                    },
                },
            };

            // Dragging the slider edits the volume; a guarded poll in Update keeps it showing the current value.
            volume.BindValueChanged(v =>
            {
                volumeReadout.Text = v.NewValue <= 0 ? "Auto" : $"{(int)Math.Round(v.NewValue * 100)}%";
                if (suppressVolume)
                    return;
                SetVolume?.Invoke(v.NewValue);
                lastVolume = v.NewValue;
            }, true);
        }

        /// <summary>The volume row: a 0-100% slider (0 = "Auto" / inherit the timing point) plus a numeric readout.</summary>
        private Drawable volumeRow() => labeledRow("Volume", new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Width = 76,
                Height = 10,
                Child = new BasicSliderBar<float>
                {
                    RelativeSizeAxes = Axes.Both,
                    Current = volume,
                    BackgroundColour = EditorTheme.Colours.Sunken,
                    SelectionColour = EditorTheme.Colours.Accent,
                },
            },
            volumeReadout = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Width = 34,
                Text = "Auto",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(numeric: true),
            },
        });

        /// <summary>The sample-index row: a -/+ stepper (0 = "Auto" / inherit the timing point) plus a numeric readout.</summary>
        private Drawable indexRow() => labeledRow("Index", new Drawable[]
        {
            new TextButton("-", () => SetIndex?.Invoke(Math.Max(0, currentIndex - 1)), 22),
            indexReadout = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Width = 30,
                Text = "Auto",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Caption(numeric: true),
            },
            new TextButton("+", () => SetIndex?.Invoke(currentIndex + 1), 22),
        });

        /// <summary>A fixed-width caption (lined up with the bank rows) followed by the given controls, laid out horizontally.</summary>
        private static Drawable labeledRow(string caption, Drawable[] controls)
        {
            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                Children = new Drawable[]
                {
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Width = 30,
                        AutoSizeAxes = Axes.Y,
                        Child = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = caption,
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                },
            };

            foreach (var c in controls)
                row.Add(c);

            return row;
        }


        private static FillFlowContainer group(string caption, Dictionary<SampleBank, Chip> into, Action<SampleBank> onClick)
        {
            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                Children = new Drawable[]
                {
                    // Fixed-width caption so the Normal / Addition chip rows line up when stacked.
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Width = 30,
                        AutoSizeAxes = Axes.Y,
                        Child = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = caption,
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                },
            };

            foreach (var (bank, label) in bank_options)
            {
                var chip = new Chip(label, () => onClick(bank));
                into[bank] = chip;
                row.Add(chip);
            }

            return row;
        }

        protected override void Update()
        {
            base.Update();
            if (StateProvider == null)
                return;

            var s = StateProvider();
            foreach (var (bank, chip) in normalChips)
                chip.SetActive(bank == s.Normal);
            foreach (var (bank, chip) in additionChips)
                chip.SetActive(bank == s.Addition);

            // Push the current volume to the slider only when it actually changed (selection swap / external edit),
            // so a value the user is dragging is never yanked back under them.
            if (Math.Abs(s.Volume - lastVolume) > 0.0005f)
            {
                suppressVolume = true;
                volume.Value = s.Volume;
                lastVolume = s.Volume;
                suppressVolume = false;
            }

            currentIndex = s.Index;
            indexReadout.Text = s.Index <= 0 ? "Auto" : s.Index.ToString();

            if (HasClip != null)
                pasteButton.SetEnabledLook(HasClip());
        }

        /// <summary>A small momentary text button (steppers, Copy/Paste); fixed width so it can hold a relative-sized background.</summary>
        private partial class TextButton : ClickableContainer
        {
            private readonly Box bg;

            public TextButton(string text, Action onClick, float width = 52)
            {
                Action = onClick;
                Width = width;
                Height = 24;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
                Children = new Drawable[]
                {
                    bg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                };
            }

            /// <summary>Dims the button when its action is unavailable (e.g. Paste with an empty clipboard).</summary>
            public void SetEnabledLook(bool enabled) => Alpha = enabled ? 1f : 0.4f;

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                bg.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
                => bg.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        /// <summary>A small toggleable square button; active = solid accent (matches the hitsound palette chips).</summary>
        private partial class Chip : ClickableContainer
        {
            private Box background = null!;
            private SpriteText label = null!;
            private readonly string text;
            private bool active;

            public Chip(string label, Action onClick)
            {
                text = label;
                Action = onClick;
                Size = new Vector2(26, 24);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    label = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                };
            }

            public void SetActive(bool value)
            {
                if (value == active)
                    return;

                active = value;
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                if (!active)
                    background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                if (!active)
                    background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            }
        }
    }
}
