using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
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
            Masking = true;
            CornerRadius = EditorTheme.Radius.Lg;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(EditorTheme.Spacing.Sm),
                    Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                    Children = new Drawable[]
                    {
                        // Bank selectors.
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                            Children = new Drawable[]
                            {
                                group("Normal", normalChips, b => SetNormalBank?.Invoke(b)),
                                group("Addition", additionChips, b => SetAdditionBank?.Invoke(b)),
                            },
                        },
                        // Hairline divider.
                        new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorTheme.Colours.Border },
                        // Controls legend (how to use the lane grid).
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                            Children = new Drawable[]
                            {
                                legendEntry("L-Click", "add"),
                                legendEntry("R-Click", "remove"),
                                legendEntry("Shift + L", "cycle bank"),
                                legendEntry("Drag", "paint (L add / R erase)"),
                            },
                        },
                    },
                },
            };
        }

        /// <summary>A keycap-style chip plus a short description, used in the controls legend.</summary>
        private static Drawable legendEntry(string keys, string description) => new FillFlowContainer
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
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Sm,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                        new SpriteText
                        {
                            Padding = new MarginPadding { Horizontal = 5, Vertical = 2 },
                            Text = keys,
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Caption(),
                        },
                    },
                },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = description,
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Caption(),
                },
            },
        };

        private static FillFlowContainer group(string caption, Dictionary<SampleBank, Chip> into, Action<SampleBank> onClick)
        {
            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Right = EditorTheme.Spacing.Xs },
                        Text = caption,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Caption(),
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
                Size = new Vector2(26, EditorTheme.Sizing.ButtonHeight);
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
