using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A flat, rounded button following the editor design system (see docs/design-guide.md). Two intents:
    /// <b>Default</b> (neutral grey that lightens on hover) and <b>Primary</b> (brand-accent fill, dark text)
    /// for the single main action in a view. The intent is inferred from the accent colour passed in - the
    /// brand accent yields a Primary button, anything else a neutral Default - so existing call sites keep
    /// working while getting the new styling. All colour/radius/motion come from <see cref="EditorTheme"/>.
    /// </summary>
    public partial class OsuButton : ClickableContainer
    {
        private enum Kind { Default, Primary, Danger }

        private string text;
        private readonly Kind kind;

        private Box background = null!;
        private SpriteText label = null!;

        /// <summary>Font size of the button label.</summary>
        public float FontSize { get; init; } = 15;

        /// <summary>Optional icon shown to the left of the label (centred together). Null = label only.</summary>
        public IconUsage? Icon { get; init; }

        /// <summary>The button's label; can be updated after construction.</summary>
        public string Text
        {
            get => text;
            set
            {
                text = value;
                if (label != null)
                    label.Text = value;
            }
        }

        /// <param name="text">Label rendered in the centre of the button.</param>
        /// <param name="accent">
        /// The brand accent yields a <b>Primary</b> button (accent fill); the error colour yields a
        /// <b>Danger</b> button (neutral at rest, red on hover); anything else a neutral <b>Default</b> one.
        /// </param>
        public OsuButton(string text, Color4 accent)
        {
            this.text = text;
            kind = accent.Equals(EditorTheme.Colours.Accent) ? Kind.Primary
                : accent.Equals(EditorTheme.Colours.Error) ? Kind.Danger
                : Kind.Default;

            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
        }

        private Color4 idleColour => kind == Kind.Primary ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control;
        private Color4 hoverColour => kind switch
        {
            Kind.Primary => EditorTheme.Colours.AccentHover,
            Kind.Danger => EditorTheme.Colours.Error,
            _ => EditorTheme.Colours.ControlHover,
        };
        private Color4 pressColour => kind switch
        {
            Kind.Primary => EditorTheme.Colours.AccentPressed,
            Kind.Danger => EditorTheme.Colours.Error,
            _ => EditorTheme.Colours.ControlActive,
        };
        private Color4 textColour => kind == Kind.Primary ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text;

        [BackgroundDependencyLoader]
        private void load()
        {
            label = new SpriteText
            {
                Text = text,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Colour = textColour,
                Font = FontUsage.Default.With(size: FontSize, weight: "SemiBold"),
            };

            // With an icon, lay the icon + label out in a centred row; without one, the label is centred alone.
            Drawable content = Icon is { } ic
                ? new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new osuTK.Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = ic,
                            Size = new osuTK.Vector2(FontSize - 1),
                            Colour = textColour,
                        },
                        label,
                    },
                }
                : label;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = idleColour,
                },
                content,
            };

            // Dim while disabled (ClickableContainer suppresses the Action automatically).
            Enabled.BindValueChanged(e => this.FadeTo(e.NewValue ? 1f : 0.45f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(hoverColour, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(idleColour, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            background.FlashColour(pressColour, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            return base.OnMouseDown(e);
        }
    }
}
