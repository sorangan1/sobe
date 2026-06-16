using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The right-docked Modding Mode panel: collapsible filter sections (by user / by type / status) at the top
    /// and a scrollable list of the matching discussion ("mod") messages below. Clicking a timestamped message
    /// seeks the playhead; untimed (general) notes are shown but not clickable. Type filters persist across maps.
    /// </summary>
    public partial class ModdingPanel : CompositeDrawable
    {
        /// <summary>Seek the editor to a given time (ms) when a timestamped message is clicked.</summary>
        public Action<double>? OnSeek;

        /// <summary>Raised whenever a filter changes, so the editor can re-apply <see cref="IsVisible"/>.</summary>
        public Action? OnFiltersChanged;

        private readonly Bindable<string> mutedTypesSetting;

        private readonly HashSet<long> mutedUsers = new HashSet<long>();
        private readonly HashSet<string> mutedTypes = new HashSet<string>();
        private bool hideResolved;

        private FillFlowContainer userFlow = null!;
        private FillFlowContainer typeFlow = null!;
        private CollapsibleSection userSection = null!;
        private CollapsibleSection typeSection = null!;
        private FillFlowContainer cardFlow = null!;
        private SpriteText headerCount = null!;
        private SpriteText emptyText = null!;

        public ModdingPanel(Bindable<string> mutedTypesSetting)
        {
            this.mutedTypesSetting = mutedTypesSetting;
            foreach (var t in mutedTypesSetting.Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                mutedTypes.Add(t.Trim());
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Lg,
                BorderThickness = 1,
                BorderColour = EditorTheme.Colours.Border,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize), // header
                            new Dimension(GridSizeMode.AutoSize), // filters
                            new Dimension(),                       // messages (fill)
                        },
                        Content = new[]
                        {
                            new Drawable[] { buildHeader() },
                            new Drawable[] { buildFilters() },
                            new Drawable[] { buildMessages() },
                        },
                    },
                },
            };
        }

        private Drawable buildHeader() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = EditorTheme.Spacing.Lg,
                    Text = "Mods",
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Heading(),
                },
                headerCount = new SpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    X = -EditorTheme.Spacing.Lg,
                    Text = "0",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Label(numeric: true),
                },
            },
        };

        private Drawable buildFilters() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Lg },
            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorTheme.Colours.Border },
                userSection = new CollapsibleSection("Users", userFlow = column(), startExpanded: false),
                typeSection = new CollapsibleSection("Type", typeFlow = column(), startExpanded: true),
                new FilterRow("Hide resolved", null, () => hideResolved, () =>
                {
                    hideResolved = !hideResolved;
                    OnFiltersChanged?.Invoke();
                }),
                new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorTheme.Colours.Border, Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs } },
            },
        };

        private Drawable buildMessages() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Md, Vertical = EditorTheme.Spacing.Md },
            Children = new Drawable[]
            {
                emptyText = new SpriteText
                {
                    X = EditorTheme.Spacing.Xs,
                    Text = "No mods to show.",
                    Colour = EditorTheme.Colours.TextFaint,
                    Font = EditorTheme.Type.Body(),
                },
                new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    ScrollbarOverlapsContent = false,
                    Child = cardFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                    },
                },
            },
        };

        /// <summary>Rebuilds the user/type filter controls from the loaded discussions (keeps persisted type mutes).</summary>
        public void SetDiscussions(IReadOnlyList<ModdingDiscussion> all)
        {
            mutedUsers.Clear();

            userFlow.Clear();
            typeFlow.Clear();

            foreach (var g in all.GroupBy(d => d.UserId).OrderByDescending(g => g.Count()))
            {
                long userId = g.Key;
                string name = g.First().Username;
                userFlow.Add(new FilterRow(name, null, () => !mutedUsers.Contains(userId), () =>
                {
                    if (!mutedUsers.Add(userId))
                        mutedUsers.Remove(userId);
                    OnFiltersChanged?.Invoke();
                }, count: g.Count()));
            }

            foreach (var type in all.Select(d => d.MessageType).Distinct().OrderBy(t => t))
            {
                string t = type;
                int n = all.Count(d => d.MessageType == t);
                typeFlow.Add(new FilterRow(ModdingDiscussion.TypeLabel(t), ModdingDiscussion.TypeColour(t), () => !mutedTypes.Contains(t), () =>
                {
                    if (!mutedTypes.Add(t))
                        mutedTypes.Remove(t);
                    persistTypes();
                    OnFiltersChanged?.Invoke();
                }, count: n));
            }

            userSection.SetCount(userFlow.Children.Count);
            typeSection.SetCount(typeFlow.Children.Count);
        }

        /// <summary>Replaces the message cards with the given (already-filtered) discussions.</summary>
        public void SetMessages(IReadOnlyList<ModdingDiscussion> visible)
        {
            cardFlow.Clear();
            headerCount.Text = visible.Count.ToString();
            emptyText.Alpha = visible.Count == 0 ? 1 : 0;

            // Timestamped first (ascending), then untimed/general notes.
            foreach (var d in visible.OrderBy(d => d.TimestampMs ?? int.MaxValue).ThenBy(d => d.Id))
                cardFlow.Add(new MessageCard(d, OnSeek));
        }

        /// <summary>Whether a discussion passes the current filters.</summary>
        public bool IsVisible(ModdingDiscussion d)
            => !mutedUsers.Contains(d.UserId)
               && !mutedTypes.Contains(d.MessageType)
               && (!hideResolved || !d.Resolved);

        private void persistTypes() => mutedTypesSetting.Value = string.Join(",", mutedTypes);

        private static FillFlowContainer column() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 1),
        };

        // ------------------------------------------------------------------ collapsible "dropdown" section

        private partial class CollapsibleSection : CompositeDrawable
        {
            private readonly string title;
            private readonly Drawable content;
            private bool expanded;

            private Container contentWrapper = null!;
            private SpriteText caret = null!;
            private SpriteText countText = null!;
            private Box headerBg = null!;

            public CollapsibleSection(string title, Drawable content, bool startExpanded)
            {
                this.title = title;
                this.content = content;
                expanded = startExpanded;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new HeaderButton(toggle, hovered => headerBg.FadeColour(hovered ? EditorTheme.Colours.Raised : EditorTheme.Colours.Surface, EditorTheme.Motion.Fast))
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 28,
                            Masking = true,
                            CornerRadius = EditorTheme.Radius.Sm,
                            Children = new Drawable[]
                            {
                                headerBg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface },
                                caret = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = expanded ? "-" : "+",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Label(numeric: true),
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    X = 16,
                                    Text = title,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Label(),
                                },
                                countText = new SpriteText
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Colour = EditorTheme.Colours.TextFaint,
                                    Font = EditorTheme.Type.Caption(numeric: true),
                                },
                            },
                        },
                        contentWrapper = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = expanded ? Axes.Y : Axes.None,
                            Masking = true,
                            Padding = new MarginPadding { Left = 16, Bottom = EditorTheme.Spacing.Xs },
                            Child = content,
                        },
                    },
                };
            }

            public void SetCount(int count) => countText.Text = count.ToString();

            private void toggle()
            {
                expanded = !expanded;
                caret.Text = expanded ? "-" : "+";
                if (expanded)
                {
                    contentWrapper.AutoSizeAxes = Axes.Y;
                }
                else
                {
                    contentWrapper.AutoSizeAxes = Axes.None;
                    contentWrapper.Height = 0;
                }
            }

            private partial class HeaderButton : Container
            {
                private readonly Action onClick;
                private readonly Action<bool> onHover;

                public HeaderButton(Action onClick, Action<bool> onHover)
                {
                    this.onClick = onClick;
                    this.onHover = onHover;
                }

                protected override bool OnClick(ClickEvent e)
                {
                    onClick();
                    return true;
                }

                protected override bool OnHover(HoverEvent e)
                {
                    onHover(true);
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e) => onHover(false);
            }
        }

        // ------------------------------------------------------------------ a single filter check row

        private partial class FilterRow : ClickableContainer
        {
            private readonly Func<bool> isOn;
            private Box check = null!;
            private Container checkBox = null!;
            private Box hoverBg = null!;

            public FilterRow(string label, Color4? dot, Func<bool> isOn, Action onToggle, int count = -1)
            {
                this.isOn = isOn;
                Action = onToggle;

                RelativeSizeAxes = Axes.X;
                Height = 24;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;

                check = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent };
                checkBox = new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 2,
                    Size = new Vector2(14),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Sm,
                    BorderThickness = 1,
                    BorderColour = EditorTheme.Colours.BorderStrong,
                    Child = check,
                };

                hoverBg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised, Alpha = 0 };

                var children = new List<Drawable> { hoverBg, checkBox };

                float textX = 24;
                if (dot is { } d)
                {
                    children.Add(new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 24,
                        Size = new Vector2(8),
                        Colour = d,
                    });
                    textX = 38;
                }

                children.Add(new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = textX,
                    Text = label,
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Body(),
                });

                if (count >= 0)
                {
                    children.Add(new SpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -2,
                        Text = count.ToString(),
                        Colour = EditorTheme.Colours.TextFaint,
                        Font = EditorTheme.Type.Caption(numeric: true),
                    });
                }

                Children = children;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                refresh();
            }

            protected override bool OnHover(HoverEvent e)
            {
                hoverBg.FadeTo(0.6f, EditorTheme.Motion.Fast);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hoverBg.FadeTo(0, EditorTheme.Motion.Fast);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                bool handled = base.OnClick(e);
                refresh();
                return handled;
            }

            private void refresh()
            {
                bool on = isOn();
                check.FadeTo(on ? 1 : 0, EditorTheme.Motion.Fast);
                checkBox.BorderColour = on ? EditorTheme.Colours.Accent : EditorTheme.Colours.BorderStrong;
            }
        }

        // ------------------------------------------------------------------ a single mod message card

        private partial class MessageCard : ClickableContainer
        {
            private readonly bool clickable;
            private Box background = null!;
            private Box stripe = null!;

            public MessageCard(ModdingDiscussion d, Action<double>? onSeek)
            {
                clickable = d.TimestampMs.HasValue;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;

                if (d.TimestampMs is { } ms)
                    Action = () => onSeek?.Invoke(ms);

                var accent = ModdingDiscussion.TypeColour(d.MessageType);
                // General (untimed) notes are visually de-emphasised so the clickable ones clearly stand out.
                var badgeColour = clickable ? accent : EditorTheme.Colours.Control;

                var headerRow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new CircularContainer
                        {
                            Size = new Vector2(24),
                            Masking = true,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = badgeColour },
                                new SpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Text = initialOf(d.Username),
                                    Colour = clickable ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Label(),
                                },
                            },
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = d.Username,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.BodyStrong(),
                                },
                                new SpriteText
                                {
                                    Text = $"{ModdingDiscussion.TypeLabel(d.MessageType)} - {relativeTime(d.CreatedAt)}",
                                    Colour = accent,
                                    Font = EditorTheme.Type.Caption(),
                                },
                            },
                        },
                    },
                };

                var metaChildren = new List<Drawable>();
                if (d.TimestampMs is { } t)
                {
                    // A pill that reads like a clickable link: the in-song timestamp.
                    metaChildren.Add(new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = EditorTheme.Radius.Sm,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(accent.R, accent.G, accent.B, 0.16f) },
                            new SpriteText
                            {
                                Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
                                Text = formatTimestamp(t),
                                Colour = accent,
                                Font = EditorTheme.Type.Caption(numeric: true),
                            },
                        },
                    });
                }
                else
                {
                    metaChildren.Add(new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "general note",
                        Colour = EditorTheme.Colours.TextFaint,
                        Font = EditorTheme.Type.Caption(),
                    });
                }

                if (d.Resolved)
                {
                    metaChildren.Add(new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "resolved",
                        Colour = EditorTheme.Colours.Velocity,
                        Font = EditorTheme.Type.Caption(),
                    });
                }

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = clickable ? EditorTheme.Colours.Raised : EditorTheme.Colours.Sunken },
                    stripe = new Box { RelativeSizeAxes = Axes.Y, Width = 3, Colour = clickable ? accent : EditorTheme.Colours.Border },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Left = EditorTheme.Spacing.Lg, Right = EditorTheme.Spacing.Md, Vertical = EditorTheme.Spacing.Md },
                        Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                        Children = new Drawable[]
                        {
                            headerRow,
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                                Children = metaChildren.ToArray(),
                            },
                            new TextFlowContainer(t => t.Font = EditorTheme.Type.Body())
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Colour = clickable ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted,
                                Text = d.Message,
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (clickable)
                {
                    background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast);
                    stripe.ResizeWidthTo(5, EditorTheme.Motion.Fast);
                }
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (clickable)
                {
                    background.FadeColour(EditorTheme.Colours.Raised, EditorTheme.Motion.Fast);
                    stripe.ResizeWidthTo(3, EditorTheme.Motion.Fast);
                }
                base.OnHoverLost(e);
            }

            private static string initialOf(string name) =>
                string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>Formats a time (ms) as the osu! editor/modding timestamp "mm:ss:fff".</summary>
        private static string formatTimestamp(double ms)
        {
            var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";
        }

        /// <summary>A short "3y / 2mo / 5d / 4h / just now" relative time from a post's creation date.</summary>
        private static string relativeTime(DateTimeOffset? at)
        {
            if (at is not { } when)
                return "";

            var span = DateTimeOffset.UtcNow - when.ToUniversalTime();
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            if (span.TotalDays >= 365) return $"{(int)(span.TotalDays / 365)}y ago";
            if (span.TotalDays >= 30) return $"{(int)(span.TotalDays / 30)}mo ago";
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d ago";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h ago";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m ago";
            return "just now";
        }
    }
}
