using System;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small modal (Ctrl+Shift+R) that rotates the current selection by a typed angle. The angle field is
    /// auto-focused; direction picks clockwise / anticlockwise and origin picks the playfield centre or the
    /// selection's centre. Enter (or Rotate) commits, Escape (or Cancel / clicking the dim) closes.
    /// </summary>
    public partial class RotationPopover : VisibilityContainer
    {
        /// <summary>Invoked on confirm with the signed angle (clockwise positive) and whether to pivot on the playfield centre.</summary>
        public Action<float, bool>? OnRotate;

        private Container panel = null!;
        private AngleTextBox angleBox = null!;
        private AnglePreview anglePreview = null!;
        private SegmentedToggle directionToggle = null!;
        private SegmentedToggle originToggle = null!;

        protected override bool StartHidden => true;

        public RotationPopover()
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
                    Width = 232,
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
                                new SpriteText
                                {
                                    Text = "Rotate by",
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(),
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 52,
                                    Children = new Drawable[]
                                    {
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Padding = new MarginPadding { Right = 60 },
                                            Child = angleBox = new AngleTextBox
                                            {
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                RelativeSizeAxes = Axes.X,
                                                Height = EditorTheme.Sizing.InputHeight,
                                                Text = "0",
                                            },
                                        },
                                        anglePreview = new AnglePreview
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Size = new Vector2(52),
                                        },
                                    },
                                },
                                directionToggle = new SegmentedToggle("CW", "CCW"),
                                originToggle = new SegmentedToggle("Playfield", "Selection"),
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Cancel", OsuColour.Surface) { Size = new Vector2(72, 28), Action = Hide },
                                        new OsuButton("Rotate", OsuColour.Pink) { Size = new Vector2(72, 28), Action = confirm },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            // Enter in the (focused) angle field commits the rotation, instead of just committing the text box.
            angleBox.OnCommit += (_, _) => confirm();
        }

        private void confirm()
        {
            if (!float.TryParse(angleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
            {
                Hide();
                return;
            }

            // Clockwise is positive (matching the editor's rotateAround); anticlockwise negates the angle.
            float signed = directionToggle.SelectedIndex == 0 ? angle : -angle;
            bool aroundPlayfield = originToggle.SelectedIndex == 0;

            OnRotate?.Invoke(signed, aroundPlayfield);
            Hide();
        }

        protected override void PopIn()
        {
            this.FadeIn(150, Easing.OutQuint);
            panel.ScaleTo(1, 300, Easing.OutQuint);

            // Auto-focus and select the angle field so the user can type immediately.
            Schedule(() =>
            {
                GetContainingFocusManager()?.ChangeFocus(angleBox);
                angleBox.SelectAll();
            });
        }

        protected override void PopOut()
        {
            this.FadeOut(150, Easing.OutQuint);
            panel.ScaleTo(0.97f, 150, Easing.OutQuint);
            GetContainingFocusManager()?.ChangeFocus(null);
        }

        protected override void Update()
        {
            base.Update();

            // Live-preview the signed angle the dialog would apply.
            float angle = float.TryParse(angleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float a) ? a : 0;
            anglePreview.SetAngle(directionToggle.SelectedIndex == 0 ? angle : -angle);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Hide();
                    return true;

                case Key.Enter:
                case Key.KeypadEnter:
                    confirm();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>A small dial that previews the selected rotation: a needle pointing at the signed angle (0° = right).</summary>
        private partial class AnglePreview : CompositeDrawable
        {
            private Container needle = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        BorderThickness = EditorTheme.Sizing.BorderThickness,
                        BorderColour = EditorTheme.Colours.Border,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Sunken },
                    },
                    // Faint reference mark at 0° (pointing right), so the needle's offset reads as the angle.
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.5f,
                        Height = 1.5f,
                        Colour = EditorTheme.Colours.Border,
                    },
                    needle = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.5f,
                        Height = 2.5f,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Accent },
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(5),
                        Colour = EditorTheme.Colours.Accent,
                    },
                };
            }

            /// <summary>Points the needle at the given angle (degrees, clockwise positive, 0° = right).</summary>
            public void SetAngle(float degrees) => needle.Rotation = degrees;
        }

        /// <summary>A text box that accepts a signed decimal angle and commits nothing on its own (read by the dialog).</summary>
        private partial class AngleTextBox : BasicTextBox
        {
            protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '.' || character == '-';
        }

        /// <summary>A two-option segmented selector; the chosen option fills with the accent colour.</summary>
        private partial class SegmentedToggle : CompositeDrawable
        {
            public int SelectedIndex { get; private set; }

            private readonly Segment[] segments;

            public SegmentedToggle(params string[] options)
            {
                RelativeSizeAxes = Axes.X;
                Height = EditorTheme.Sizing.ButtonHeight;

                var flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                };

                segments = new Segment[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    int index = i;
                    var seg = new Segment(options[i], () => select(index))
                    {
                        RelativeSizeAxes = Axes.Both,
                        // Equal fractional widths summing to 1 (no flow spacing); the gap comes from each
                        // segment's internal padding, so the fractions never exceed the container width.
                        Width = 1f / options.Length,
                    };
                    segments[i] = seg;
                    flow.Add(seg);
                }

                InternalChild = flow;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                select(0);
            }

            private void select(int index)
            {
                SelectedIndex = index;
                for (int i = 0; i < segments.Length; i++)
                    segments[i].SetActive(i == index);
            }

            private partial class Segment : ClickableContainer
            {
                private Box background = null!;
                private SpriteText text = null!;
                private readonly string label;
                private bool active;

                public Segment(string label, Action onClick)
                {
                    this.label = label;
                    Action = onClick;
                    // Inset the visible fill so neighbouring segments show a small gap between them.
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
                            text = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = label,
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Label(),
                            },
                        },
                    };
                }

                public void SetActive(bool value)
                {
                    active = value;
                    background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    text.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
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
