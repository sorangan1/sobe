using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>A themed single-line text box for the gallery (search, rename, new-collection), with optional auto-focus.</summary>
    public partial class GalleryTextBox : BasicTextBox
    {
        public Action<string>? OnCommitText;
        public bool AutoFocus;

        public GalleryTextBox()
        {
            Masking = true;
            CornerRadius = EditorTheme.Radius.Sm;
            BackgroundUnfocused = EditorTheme.Colours.Sunken;
            BackgroundFocused = EditorTheme.Colours.Control;
            Height = EditorTheme.Sizing.InputHeight;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            OnCommit += (_, _) => OnCommitText?.Invoke(Text);
            if (AutoFocus)
                Schedule(() => GetContainingFocusManager()?.ChangeFocus(this));
        }
    }

    /// <summary>A round-ish "X" button used to dismiss the gallery.</summary>
    public partial class CloseButton : ClickableContainer
    {
        private Box background = null!;

        public CloseButton(Action onClick)
        {
            Action = onClick;
            Size = new Vector2(28);
            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control, Alpha = 0 },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "x",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.BodyStrong(),
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeTo(1, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) =>
            background.FadeTo(0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
    }

    /// <summary>A row in the gallery's left rail: a collection (or "All"), selectable, with an optional delete affordance.</summary>
    public partial class RailItem : ClickableContainer
    {
        private readonly string label;
        private readonly bool selected;
        private readonly Action? onDelete;

        private Box background = null!;
        private Box accentBar = null!;

        public RailItem(string label, bool selected, Action onClick, Action? onDelete = null)
        {
            this.label = label;
            this.selected = selected;
            this.onDelete = onDelete;
            Action = onClick;
            RelativeSizeAxes = Axes.X;
            Height = 32;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent, Alpha = selected ? 0.12f : 0 };
            accentBar = new Box { RelativeSizeAxes = Axes.Y, Width = 3, Colour = EditorTheme.Colours.Accent, Alpha = selected ? 1 : 0 };

            var children = new List<Drawable>
            {
                background,
                accentBar,
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = EditorTheme.Spacing.Md },
                    Text = label,
                    Colour = selected ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
            };

            if (onDelete != null)
            {
                children.Add(new CloseButton(() => onDelete())
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Size = new Vector2(20),
                    Margin = new MarginPadding { Right = EditorTheme.Spacing.Xs },
                });
            }

            Children = children.ToArray();
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!selected)
                background.FadeTo(0.06f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (!selected)
                background.FadeTo(0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            base.OnHoverLost(e);
        }
    }

    /// <summary>
    /// One pattern in the gallery grid: a preview thumbnail, an inline-renameable name, and a footer row of
    /// actions (Add to map, Duplicate, Move to collection, Delete).
    /// </summary>
    public partial class PatternCard : CompositeDrawable
    {
        public Action? OnAdd;
        public Action? OnDuplicate;
        public Action? OnDelete;
        public Action<string>? OnRename;
        public Action<Guid?>? OnMove;

        private readonly PatternSummary summary;
        private readonly IReadOnlyList<PatternCollectionInfo> collections;

        private Container previewArea = null!;
        private Container nameArea = null!;
        private Container? movePopup;

        public PatternCard(PatternSummary summary, IReadOnlyList<PatternCollectionInfo> collections)
        {
            this.summary = summary;
            this.collections = collections;
            Size = new Vector2(176, 168);
            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        previewArea = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 104,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xs),
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = EditorTheme.Radius.Sm,
                                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                            },
                        },
                        nameArea = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 26,
                            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Sm },
                        },
                        buildFooter(),
                    },
                },
            };

            showName();
        }

        /// <summary>Replaces the placeholder with the rendered pattern once its content has been fetched.</summary>
        public void SetPreview(IReadOnlyList<HitObjectModel> objects)
        {
            previewArea.Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Sm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                    new PatternPreview(objects) { RelativeSizeAxes = Axes.Both },
                },
            };
        }

        private void showName()
        {
            nameArea.Child = new NameLabel(summary.Name, beginRename);
        }

        private void beginRename()
        {
            nameArea.Child = new GalleryTextBox
            {
                RelativeSizeAxes = Axes.X,
                Text = summary.Name,
                AutoFocus = true,
                OnCommitText = name =>
                {
                    summary.Name = string.IsNullOrWhiteSpace(name) ? summary.Name : name.Trim();
                    OnRename?.Invoke(summary.Name);
                    showName();
                },
            };
        }

        private Drawable buildFooter() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
            Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Sm, Top = EditorTheme.Spacing.Xs },
            Children = new Drawable[]
            {
                new CardAction("Add", EditorTheme.Colours.Accent, () => OnAdd?.Invoke()),
                new CardAction("Dup", EditorTheme.Colours.Control, () => OnDuplicate?.Invoke()),
                new CardAction("Move", EditorTheme.Colours.Control, toggleMovePopup),
                new CardAction("Del", EditorTheme.Colours.Error, () => OnDelete?.Invoke()),
            },
        };

        private void toggleMovePopup()
        {
            if (movePopup != null)
            {
                movePopup.Expire();
                movePopup = null;
                return;
            }

            var items = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
            };
            items.Add(new MoveItem("Move to:", null));
            items.Add(new MoveItem("(None)", () => move(null)));
            foreach (var c in collections)
            {
                var id = c.Id;
                items.Add(new MoveItem(c.Name, () => move(id)));
            }

            // The card is masked (for its rounded corners), so a popup hanging below it would be clipped;
            // overlay it inside the card instead, dimming the card and centring a scrollable menu.
            AddInternal(movePopup = new MovePopupLayer(() => { movePopup?.Expire(); movePopup = null; })
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0, 0, 0, 0.55f) },
                    new BasicScrollContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(150, 150),
                        ScrollbarVisible = false,
                        Child = items,
                    },
                },
            });
        }

        private void move(Guid? colId)
        {
            movePopup?.Expire();
            movePopup = null;
            OnMove?.Invoke(colId);
        }

        /// <summary>The pattern's name as a click-to-rename label.</summary>
        private partial class NameLabel : ClickableContainer
        {
            public NameLabel(string text, Action onClick)
            {
                Action = onClick;
                RelativeSizeAxes = Axes.Both;
                // Mask the row and let the text auto-size; a relative-width SpriteText with Truncate measured 0
                // wide here and collapsed every name to "...". Masking clips an over-long name at the card edge.
                Masking = true;
                Child = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = text,
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.BodyStrong(),
                };
            }
        }

        /// <summary>A compact coloured action chip in the card footer.</summary>
        private partial class CardAction : ClickableContainer
        {
            private readonly Color4 accent;
            private Box background = null!;

            public CardAction(string label, Color4 accent, Action onClick)
            {
                this.accent = accent;
                Action = onClick;
                AutoSizeAxes = Axes.X;
                Height = 22;
                Masking = true;
                CornerRadius = EditorTheme.Radius.Sm;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = accent, Alpha = accent.Equals(EditorTheme.Colours.Accent) ? 0.9f : 0.18f },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Sm },
                        Text = label,
                        Colour = accent.Equals(EditorTheme.Colours.Accent) ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeTo(accent.Equals(EditorTheme.Colours.Accent) ? 1f : 0.35f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) =>
                background.FadeTo(accent.Equals(EditorTheme.Colours.Accent) ? 0.9f : 0.18f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        /// <summary>A row in the "Move to collection" popup. A null action makes it a non-clickable header.</summary>
        private partial class MoveItem : ClickableContainer
        {
            private readonly bool header;
            private Box background = null!;

            public MoveItem(string label, Action? onClick)
            {
                header = onClick == null;
                Action = onClick;
                Width = 140;
                Height = 26;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent, Alpha = 0 },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Sm },
                        Text = label,
                        Colour = header ? EditorTheme.Colours.TextMuted : EditorTheme.Colours.Text,
                        Font = header ? EditorTheme.Type.Label() : EditorTheme.Type.Body(),
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!header)
                    background.FadeTo(0.15f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) =>
                background.FadeTo(0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        /// <summary>The dim layer behind the move menu; clicking outside the menu (i.e. on the dim) closes it.</summary>
        private partial class MovePopupLayer : Container
        {
            private readonly Action close;

            public MovePopupLayer(Action close)
            {
                this.close = close;
            }

            protected override bool OnClick(ClickEvent e)
            {
                close();
                return true;
            }
        }
    }
}
