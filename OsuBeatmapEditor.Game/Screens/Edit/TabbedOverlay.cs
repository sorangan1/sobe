using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A centred modal with a left-hand section sidebar and the selected section's scrollable content,
    /// styled to the editor design system (see docs/design-guide.md): a neutral sidebar, an accent-marked
    /// active nav item, a per-section content header, and hairline dividers. Reopens to the last-used section.
    /// </summary>
    public abstract partial class TabbedOverlay : VisibilityContainer
    {
        private const float sidebar_width = 196f;
        private const float content_header = 46f;

        private readonly Dictionary<string, NavItem> navItems = new Dictionary<string, NavItem>();
        private (string name, IconUsage icon, Func<Drawable> content)[] sections = Array.Empty<(string, IconUsage, Func<Drawable>)>();

        private Container panel = null!;
        private Container contentContainer = null!;
        private FillFlowContainer nav = null!;

        protected abstract string Heading { get; }
        protected abstract (string name, IconUsage icon, Func<Drawable> content)[] CreateSections();

        /// <summary>Optional persisted store for the last-opened section.</summary>
        protected virtual Bindable<string>? LastSectionStore => null;

        protected override bool StartHidden => true;

        protected TabbedOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            sections = CreateSections();

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(EditorTheme.Sizing.OverlayWidth, EditorTheme.Sizing.OverlayHeight),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Child = new PopoverContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },

                            // --- Left sidebar: brand title + section navigation, on a raised surface. ---
                            new Container
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = sidebar_width,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl, Top = EditorTheme.Spacing.Xl, Bottom = EditorTheme.Spacing.Md },
                                                Text = Heading,
                                                Colour = EditorTheme.Colours.Accent,
                                                Font = EditorTheme.Type.Title(),
                                            },
                                            nav = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(0, 2),
                                            },
                                        },
                                    },
                                    // Right-edge divider separating sidebar from content.
                                    new Box
                                    {
                                        Anchor = Anchor.TopRight,
                                        Origin = Anchor.TopRight,
                                        RelativeSizeAxes = Axes.Y,
                                        Width = EditorTheme.Sizing.BorderThickness,
                                        Colour = EditorTheme.Colours.Border,
                                    },
                                },
                            },

                            // --- Content area (filled per section in showSection). ---
                            contentContainer = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Left = sidebar_width },
                            },
                        },
                    },
                },
            };

            foreach (var (name, icon, _) in sections)
            {
                var item = new NavItem(name, icon, () => showSection(name));
                navItems[name] = item;
                nav.Add(item);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            string? stored = LastSectionStore?.Value;
            string initial = stored is { Length: > 0 } && sections.Any(s => s.name == stored)
                ? stored
                : sections[0].name;

            showSection(initial);
        }

        private void showSection(string name)
        {
            if (LastSectionStore != null)
                LastSectionStore.Value = name;

            foreach (var (key, item) in navItems)
                item.SetSelected(key == name);

            var section = sections.First(s => s.name == name);

            // Per-section content: a header (icon + section name + hairline rule) over a scrollable body.
            contentContainer.Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl, Top = EditorTheme.Spacing.Lg },
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Sm + 2, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = section.icon,
                                Size = new Vector2(16),
                                Colour = EditorTheme.Colours.Accent,
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = name,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Heading(),
                            },
                        },
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = EditorTheme.Sizing.BorderThickness,
                        Y = content_header,
                        Colour = EditorTheme.Colours.Border,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = content_header },
                        Child = new BasicScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarVisible = false,
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Xl, Top = EditorTheme.Spacing.Lg, Bottom = EditorTheme.Spacing.Xl },
                                Child = section.content(),
                            },
                        },
                    },
                },
            };
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, 400, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.97f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>A selectable section entry in the sidebar: an accent bar + icon + text, with hover/selected states.</summary>
        private partial class NavItem : ClickableContainer
        {
            private readonly string label;
            private readonly IconUsage icon;
            private Box background = null!;
            private Box accentBar = null!;
            private SpriteText text = null!;
            private SpriteIcon iconSprite = null!;
            private bool selected;

            public NavItem(string label, IconUsage icon, Action onClick)
            {
                this.label = label;
                this.icon = icon;
                Action = onClick;
                RelativeSizeAxes = Axes.X;
                Height = 38;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent, Alpha = 0 },
                    accentBar = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 3,
                        Colour = EditorTheme.Colours.Accent,
                        Alpha = 0,
                    },
                    // Icon + label in a row, so a fixed icon column keeps every label aligned.
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = EditorTheme.Spacing.Xl },
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Md + 2, 0),
                        Children = new Drawable[]
                        {
                            iconSprite = new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = icon,
                                Size = new Vector2(15),
                                Colour = EditorTheme.Colours.TextMuted,
                            },
                            text = new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = label,
                                Colour = EditorTheme.Colours.TextMuted,
                                Font = EditorTheme.Type.Body(),
                            },
                        },
                    },
                };
            }

            public void SetSelected(bool value)
            {
                selected = value;
                accentBar.FadeTo(value ? 1 : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                background.FadeTo(value ? 0.12f : 0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                var c = value ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted;
                text.FadeColour(c, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                // The icon picks up the accent when selected, a quiet way to reinforce the active section.
                iconSprite.FadeColour(value ? EditorTheme.Colours.Accent : EditorTheme.Colours.TextMuted, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                {
                    background.FadeTo(0.06f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    text.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    iconSprite.FadeColour(EditorTheme.Colours.Text, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                {
                    background.FadeTo(0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    text.FadeColour(EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    iconSprite.FadeColour(EditorTheme.Colours.TextMuted, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }
                base.OnHoverLost(e);
            }
        }

        /// <summary>Swallows clicks so interacting with the panel doesn't dismiss the overlay.</summary>
        protected partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
