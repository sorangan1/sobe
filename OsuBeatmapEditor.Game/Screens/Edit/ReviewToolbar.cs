using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The left-docked Review controls panel (under the Review tool box): the modder's identity (name + personal
    /// colour, both persisted), a "show notes always" toggle, and Export / Import of the shareable
    /// <c>.sobemod</c> layer. Purely a control surface - behaviour is delegated through the exposed actions.
    /// </summary>
    public partial class ReviewToolbar : CompositeDrawable
    {
        public const float WIDTH = 150;

        public Action? OnExport;
        public Action? OnImport;

        // A small palette people cycle through to pick a personal note colour.
        private static readonly string[] palette =
        {
            "FF66AB", "4FB3FF", "52D38C", "FFB454", "C792FF", "FF5C6C", "37E0D0", "F5F5F5",
        };

        private readonly Bindable<string> authorName;
        private readonly Bindable<Colour4> authorColour;
        private readonly BindableBool showAlways;
        private readonly string? lockedName;

        private Box swatch = null!;

        /// <param name="lockedName">The osu! account name when logged in (shown read-only); null = offline, editable.</param>
        public ReviewToolbar(Bindable<string> authorName, Bindable<Colour4> authorColour, BindableBool showAlways, string? lockedName)
        {
            this.authorName = authorName;
            this.authorColour = authorColour;
            this.showAlways = showAlways;
            this.lockedName = lockedName;
            AutoSizeAxes = Axes.Y;
            Width = WIDTH;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(EditorTheme.Spacing.Sm),
                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "REVIEW",
                                Colour = EditorTheme.Colours.Accent,
                                Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                            },
                            // Identity row: colour swatch + the modder name (read-only when logged in).
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                                Children = new Drawable[]
                                {
                                    new ColourSwatch(cycleColour)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(18),
                                        SetBox = b => swatch = b,
                                    },
                                    nameControl(),
                                },
                            },
                            // Show-always toggle row.
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                                Children = new Drawable[]
                                {
                                    new IconToggleButton(showAlways, FontAwesome.Solid.Eye, "Keep notes visible even when not in Review mode", 24)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                    new SpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "Show always",
                                        Colour = EditorTheme.Colours.TextMuted,
                                        Font = EditorTheme.Type.Caption(),
                                    },
                                },
                            },
                            new OsuButton("Import", OsuColour.Surface)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 26,
                                FontSize = 12,
                                Action = () => OnImport?.Invoke(),
                            },
                            new OsuButton("Export", OsuColour.Pink)
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 26,
                                FontSize = 12,
                                Action = () => OnExport?.Invoke(),
                            },
                        },
                    },
                },
            };

            authorColour.BindValueChanged(c => { if (swatch != null) swatch.Colour = c.NewValue; }, true);
        }

        private Drawable nameControl()
        {
            // Logged in to osu!: the name is fixed to the account (read-only). Offline: editable.
            if (lockedName != null)
                return new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = lockedName,
                    Colour = EditorTheme.Colours.Text,
                    Font = FontUsage.Default.With(size: 13, weight: "SemiBold"),
                };

            return new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Width = WIDTH - 18 - EditorTheme.Spacing.Xs - EditorTheme.Spacing.Sm * 2,
                Height = EditorTheme.Sizing.InputHeight,
                Child = new EditorTextBox(authorName)
                {
                    RelativeSizeAxes = Axes.Both,
                    PlaceholderText = "your name",
                },
            };
        }

        private void cycleColour()
        {
            string current = authorColour.Value.ToHex().TrimStart('#').ToUpperInvariant();
            int idx = Array.IndexOf(palette, current);
            authorColour.Value = Colour4.FromHex(palette[(idx + 1) % palette.Length]);
        }

        /// <summary>A clickable square showing the current author colour; clicking cycles to the next palette entry.</summary>
        private partial class ColourSwatch : CompositeDrawable
        {
            private readonly Action onClick;
            public Action<Box>? SetBox;

            public ColourSwatch(Action onClick)
            {
                this.onClick = onClick;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var box = new Box { RelativeSizeAxes = Axes.Both };
                SetBox?.Invoke(box);
                InternalChild = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    BorderThickness = 1.5f,
                    BorderColour = EditorTheme.Colours.BorderStrong,
                    Child = box,
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.ScaleTo(1.12f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => this.ScaleTo(1f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }
    }
}
