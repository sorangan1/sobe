using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A small, reusable yes/no confirmation modal for destructive or irreversible actions
    /// (e.g. deleting a beatmap set or difficulty).
    /// </summary>
    public partial class ConfirmOverlay : VisibilityContainer
    {
        private Container panel = null!;
        private SpriteText titleText = null!;
        private SpriteText messageText = null!;
        private OsuButton confirmButton = null!;

        private Action? onConfirm;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = osuTK.Graphics.Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 480,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Raised,
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                            Children = new Drawable[]
                            {
                                titleText = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Title(),
                                },
                                messageText = new SpriteText
                                {
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Body(),
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Cancel", OsuColour.Surface)
                                        {
                                            Size = new Vector2(120, 44),
                                            Action = () => Hide(),
                                        },
                                        confirmButton = new OsuButton("Confirm", EditorTheme.Colours.Error)
                                        {
                                            Size = new Vector2(150, 44),
                                            Action = confirm,
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>Opens the dialog with the given copy; <paramref name="confirmAction"/> runs if confirmed.</summary>
        public void Show(string title, string message, string confirmLabel, Action confirmAction)
        {
            titleText.Text = title;
            messageText.Text = message;
            confirmButton.Text = confirmLabel;
            onConfirm = confirmAction;
            Show();
        }

        private void confirm()
        {
            var action = onConfirm;
            Hide();
            action?.Invoke();
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
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

        /// <summary>Swallows clicks inside the panel so they don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
