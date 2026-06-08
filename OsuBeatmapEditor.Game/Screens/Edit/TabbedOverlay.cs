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
    /// A centred modal with a left-hand section menu and the selected section's scrollable content.
    /// Reopens to the last-used section. Subclasses provide the sections and (optionally) a store to
    /// remember the last section across opens.
    /// </summary>
    public abstract partial class TabbedOverlay : VisibilityContainer
    {
        private readonly Dictionary<string, NavItem> navItems = new Dictionary<string, NavItem>();
        private (string name, Func<Drawable> content)[] sections = Array.Empty<(string, Func<Drawable>)>();

        private Container panel = null!;
        private Container contentContainer = null!;
        private FillFlowContainer nav = null!;

        protected abstract string Heading { get; }
        protected abstract (string name, Func<Drawable> content)[] CreateSections();

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
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.58f, 0.72f),
                    Masking = true,
                    CornerRadius = 12,
                    Child = new PopoverContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundDark },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = 180,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundRaised },
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Children = new Drawable[]
                                        {
                                            new SpriteText
                                            {
                                                Margin = new MarginPadding { Left = 18, Top = 16, Bottom = 8 },
                                                Text = Heading,
                                                Colour = OsuColour.Pink,
                                                Font = FontUsage.Default.With(size: 22, weight: "Bold"),
                                            },
                                            nav = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                            },
                                        },
                                    },
                                },
                            },
                            contentContainer = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Left = 180 },
                            },
                        },
                    },
                },
            };

            foreach (var (name, _) in sections)
            {
                var item = new NavItem(name, () => showSection(name));
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

            contentContainer.Child = new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ScrollbarVisible = false,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding(24),
                    Child = sections.First(s => s.name == name).content(),
                },
            };
        }

        protected override void PopIn()
        {
            this.FadeIn(200, Easing.OutQuint);
            panel.ScaleTo(1, 400, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(200, Easing.OutQuint);
            panel.ScaleTo(0.97f, 200, Easing.OutQuint);
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

        /// <summary>A selectable section entry in the left navigation.</summary>
        private partial class NavItem : ClickableContainer
        {
            private readonly string label;
            private Box background = null!;
            private bool selected;

            public NavItem(string label, Action onClick)
            {
                this.label = label;
                Action = onClick;
                RelativeSizeAxes = Axes.X;
                Height = 40;
            }

            [osu.Framework.Allocation.BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Pink, Alpha = 0 },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = 18 },
                        Text = label,
                        Colour = OsuColour.Text,
                        Font = FontUsage.Default.With(size: 17, weight: "SemiBold"),
                    },
                };
            }

            public void SetSelected(bool value)
            {
                selected = value;
                background.FadeTo(value ? 0.3f : 0, 150, Easing.OutQuint);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!selected)
                    background.FadeTo(0.12f, 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!selected)
                    background.FadeTo(0, 100);
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
