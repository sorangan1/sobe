using System;
using System.Collections.Generic;
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
    /// The left-panel hitsound palette (mirrors osu!lazer's hitsound toolbar). Three addition toggles
    /// (Whistle / Finish / Clap) plus a Normal and an Addition sample-bank selector (Normal / Soft / Drum).
    /// Edits apply to the current selection, or - when nothing is selected - set the defaults for new objects.
    /// The palette reflects the selection's (or the pending) hitsounds, polled each frame via <see cref="StateProvider"/>.
    /// </summary>
    public partial class HitsoundPanel : CompositeDrawable
    {
        /// <summary>Returns the hitsounds the palette should currently display.</summary>
        public Func<EditorScreen.HitsoundState>? StateProvider;

        /// <summary>Raised when an addition (whistle/finish/clap) chip is clicked, with its hitSound bit.</summary>
        public Action<int>? ToggleAddition;

        /// <summary>Raised when a normal-bank chip is clicked.</summary>
        public Action<SampleBank>? SetNormalBank;

        /// <summary>Raised when an addition-bank chip is clicked.</summary>
        public Action<SampleBank>? SetAdditionBank;

        private const int whistle = 0b0010, finish = 0b0100, clap = 0b1000;
        private const float panel_width = 124f;

        private Chip whistleChip = null!, finishChip = null!, clapChip = null!;
        private readonly Dictionary<SampleBank, Chip> normalChips = new Dictionary<SampleBank, Chip>();
        private readonly Dictionary<SampleBank, Chip> additionChips = new Dictionary<SampleBank, Chip>();

        public HitsoundPanel()
        {
            AutoSizeAxes = Axes.Y;
            Width = panel_width;
            Masking = true;
            CornerRadius = EditorTheme.Radius.Lg;

            // Whistle / Finish / Clap shown as icons (with the letter kept as the tooltip).
            whistleChip = new Chip(FontAwesome.Solid.VolumeUp, "Whistle", () => ToggleAddition?.Invoke(whistle));
            finishChip = new Chip(FontAwesome.Solid.Star, "Finish", () => ToggleAddition?.Invoke(finish));
            clapChip = new Chip(FontAwesome.Solid.HandPaper, "Clap", () => ToggleAddition?.Invoke(clap));

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(EditorTheme.Spacing.Md),
                    Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                    Children = new Drawable[]
                    {
                        caption("HITSOUNDS"),
                        chipRow(whistleChip, finishChip, clapChip),
                        caption("Sample"),
                        bankRow(normalChips, b => SetNormalBank?.Invoke(b)),
                        caption("Addition"),
                        bankRow(additionChips, b => SetAdditionBank?.Invoke(b)),
                    },
                },
            };
        }

        private static SpriteText caption(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Caption(),
        };

        private static FillFlowContainer chipRow(params Drawable[] chips) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Full,
            Spacing = new Vector2(EditorTheme.Spacing.Xs),
            Children = chips,
        };

        private static FillFlowContainer bankRow(Dictionary<SampleBank, Chip> into, Action<SampleBank> onClick)
        {
            var row = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Full,
                Spacing = new Vector2(EditorTheme.Spacing.Xs),
            };

            foreach (var (bank, label) in new[] { (SampleBank.Normal, "N"), (SampleBank.Soft, "S"), (SampleBank.Drum, "D") })
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

            whistleChip.SetActive((s.HitSound & whistle) != 0);
            finishChip.SetActive((s.HitSound & finish) != 0);
            clapChip.SetActive((s.HitSound & clap) != 0);

            foreach (var (bank, chip) in normalChips)
                chip.SetActive(bank == s.Normal);
            foreach (var (bank, chip) in additionChips)
                chip.SetActive(bank == s.Addition);
        }

        /// <summary>A small toggleable square button; active = solid accent (matches the tool rows). Shows either a
        /// letter or an icon; icon chips carry a tooltip with their full name.</summary>
        private partial class Chip : ClickableContainer, osu.Framework.Graphics.Cursor.IHasTooltip
        {
            private Box background = null!;
            private Drawable content = null!;
            private readonly string? text;
            private readonly IconUsage? icon;
            private bool active;

            public osu.Framework.Localisation.LocalisableString TooltipText { get; }

            public Chip(string label, Action onClick)
            {
                text = label;
                TooltipText = string.Empty;
                init(onClick);
            }

            public Chip(IconUsage icon, string tooltip, Action onClick)
            {
                this.icon = icon;
                TooltipText = tooltip;
                init(onClick);
            }

            private void init(Action onClick)
            {
                Action = onClick;

                // Three chips share a 124-wide panel inset by 8 each side, with 4px gaps → ~33 each.
                Size = new Vector2(32, EditorTheme.Sizing.ButtonHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                content = icon is { } i
                    ? new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(13),
                        Icon = i,
                        Colour = EditorTheme.Colours.Text,
                    }
                    : new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text ?? string.Empty,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    };

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    content,
                };
            }

            public void SetActive(bool value)
            {
                if (value == active)
                    return;

                active = value;
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                content.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
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
