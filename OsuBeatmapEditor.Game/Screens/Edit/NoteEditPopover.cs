using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Annotations;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small modal for writing/editing a Review note's text. Opened when a note is created or clicked in Review
    /// mode. Enter or "Done" commits the text; "Delete" removes the note; Escape / clicking the dim cancels (a
    /// freshly-created, still-empty note is removed on cancel so stray empty pins aren't left behind).
    /// </summary>
    public partial class NoteEditPopover : VisibilityContainer
    {
        /// <summary>Invoked when the note is committed, with the new text + type (the editor applies them, snapshotting first).</summary>
        public Action<Annotation, string, string>? OnSaved;

        /// <summary>Invoked with the note when it should be deleted.</summary>
        public Action<Annotation>? OnDeleted;

        private Container panel = null!;
        private BasicTextBox textBox = null!;
        private SpriteText header = null!;
        private TypeSelector typeSelector = null!;

        private Annotation? current;
        private bool isNew;

        protected override bool StartHidden => true;

        public NoteEditPopover()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Y,
                    Width = 420,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(EditorTheme.Spacing.Md),
                            Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                            Children = new Drawable[]
                            {
                                header = new SpriteText
                                {
                                    Text = "Note",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                },
                                typeSelector = new TypeSelector(),
                                textBox = new BasicTextBox
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 34,
                                    PlaceholderText = "Type your comment...",
                                },
                                // A Container (not a FillFlow) so the Delete button can sit left while the Cancel/Done
                                // group is right-aligned - mixed anchors aren't allowed inside one FillFlowContainer.
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 28,
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Delete", EditorTheme.Colours.Error)
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Size = new Vector2(80, 28),
                                            Action = deleteCurrent,
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
                                                new OsuButton("Cancel", OsuColour.Surface) { Size = new Vector2(72, 28), Action = cancel },
                                                new OsuButton("Done", OsuColour.Pink) { Size = new Vector2(72, 28), Action = confirm },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            textBox.OnCommit += (_, _) => confirm();
        }

        /// <summary>Opens the editor for a note. <paramref name="isNew"/> notes are deleted if cancelled empty.</summary>
        public void OpenFor(Annotation annotation, bool isNew)
        {
            current = annotation;
            this.isNew = isNew;
            header.Text = isNew ? "New note" : "Edit note";
            textBox.Text = annotation.Text;
            typeSelector.Select(annotation.Type);
            Show();
        }

        private void confirm()
        {
            if (current == null)
            {
                Hide();
                return;
            }

            // Hand the new values to the editor, which snapshots (for undo) before applying + handles empty=delete.
            OnSaved?.Invoke(current, textBox.Text, typeSelector.Selected);
            current = null;
            Hide();
        }

        private void deleteCurrent()
        {
            if (current != null)
                OnDeleted?.Invoke(current);
            current = null;
            Hide();
        }

        private void cancel()
        {
            // Cancelling a brand-new note discards it; cancelling an edit leaves the existing note untouched.
            if (current != null && isNew)
                OnDeleted?.Invoke(current);
            current = null;
            Hide();
        }

        protected override void PopIn()
        {
            this.FadeIn(150, Easing.OutQuint);
            panel.ScaleTo(1, 300, Easing.OutQuint);
            Schedule(() =>
            {
                GetContainingFocusManager()?.ChangeFocus(textBox);
                textBox.SelectAll();
            });
        }

        protected override void PopOut()
        {
            this.FadeOut(150, Easing.OutQuint);
            panel.ScaleTo(0.97f, 150, Easing.OutQuint);
            GetContainingFocusManager()?.ChangeFocus(null);
        }

        protected override bool OnClick(ClickEvent e)
        {
            cancel();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    cancel();
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                    confirm();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>A row of four note-type buttons (Note / Praise / Problem / Suggestion); the chosen one is lit.</summary>
        private partial class TypeSelector : CompositeDrawable
        {
            public string Selected { get; private set; } = Annotation.TypeNote;

            private readonly System.Collections.Generic.Dictionary<string, TypeButton> buttons = new System.Collections.Generic.Dictionary<string, TypeButton>();

            public TypeSelector()
            {
                RelativeSizeAxes = Axes.X;
                Height = 30;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                };

                foreach (string type in ReviewIcons.AllTypes)
                {
                    var btn = new TypeButton(type, () => Select(type)) { Width = 1f / ReviewIcons.AllTypes.Length };
                    buttons[type] = btn;
                    flow.Add(btn);
                }

                InternalChild = flow;
            }

            public void Select(string type)
            {
                Selected = buttons.ContainsKey(type) ? type : Annotation.TypeNote;
                foreach (var (t, b) in buttons)
                    b.SetActive(t == Selected);
            }

            private partial class TypeButton : ClickableContainer
            {
                private readonly string type;
                private Box background = null!;
                private SpriteIcon icon = null!;
                private SpriteText label = null!;
                private bool active;

                public TypeButton(string type, Action onClick)
                {
                    this.type = type;
                    Action = onClick;
                    RelativeSizeAxes = Axes.Both;
                    Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Xxs };
                }

                [BackgroundDependencyLoader]
                private void load()
                {
                    InternalChild = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = EditorTheme.Radius.Md,
                        Children = new Drawable[]
                        {
                            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(4, 0),
                                Children = new Drawable[]
                                {
                                    icon = new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = ReviewIcons.For(type),
                                        Size = new Vector2(11),
                                        Colour = EditorTheme.Colours.TextMuted,
                                    },
                                    label = new SpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = ReviewIcons.Label(type),
                                        Colour = EditorTheme.Colours.Text,
                                        Font = EditorTheme.Type.Label(),
                                    },
                                },
                            },
                        },
                    };
                }

                public void SetActive(bool value)
                {
                    active = value;
                    background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    icon.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.TextMuted, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    label.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                }

                protected override bool OnHover(HoverEvent e)
                {
                    if (!active)
                        background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    return true;
                }

                protected override void OnHoverLost(HoverLostEvent e)
                {
                    if (!active)
                        background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                }
            }
        }
    }
}
