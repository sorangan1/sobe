using System;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>Layout helpers for settings sections.</summary>
    public static class SettingsLayout
    {
        /// <summary>A fixed-height row with a left label and a right-aligned control.</summary>
        public static Drawable LabeledRow(string label, Drawable control, float height = 34)
        {
            control.Anchor = Anchor.CentreRight;
            control.Origin = Anchor.CentreRight;

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = height,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Colour = OsuColour.Text,
                        Font = FontUsage.Default.With(size: 17, weight: "SemiBold"),
                    },
                    control,
                },
            };
        }
    }

    /// <summary>A basic text box two-way bound to a string setting.</summary>
    public partial class EditorTextBox : BasicTextBox
    {
        private readonly Bindable<string> bindable;

        public EditorTextBox(Bindable<string> bindable)
        {
            this.bindable = bindable;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Text = bindable.Value;
            Current.BindTo(bindable);
        }
    }

    /// <summary>
    /// A small numeric text box bound to a <see cref="BindableFloat"/>, used to type a difficulty
    /// value (HP/CS/AR/OD) directly. Commits on focus loss / Enter, clamps to the bindable's range,
    /// and reflects external changes (e.g. dragging the matching slider).
    /// </summary>
    public partial class NumberBox : BasicTextBox
    {
        private readonly BindableFloat bindable;

        public NumberBox(BindableFloat bindable)
        {
            this.bindable = bindable;
            CommitOnFocusLost = true;
            LengthLimit = 5;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            updateText();
            bindable.BindValueChanged(_ => updateText());
        }

        protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '.';

        // Always use '.' as the decimal separator, independent of the system locale.
        private void updateText() => Text = bindable.Value.ToString("0.0#", CultureInfo.InvariantCulture);

        protected override void Commit()
        {
            if (float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                bindable.Value = Math.Clamp(value, bindable.MinValue, bindable.MaxValue);

            updateText();
            base.Commit();
        }
    }

    /// <summary>A compact colour swatch that opens an HSV picker popover when clicked.</summary>
    public partial class ColourSwatch : Container, IHasPopover
    {
        private readonly Bindable<Colour4> colour;

        public ColourSwatch(Bindable<Colour4> colour)
        {
            this.colour = colour;
            Size = new Vector2(64, 26);
            Masking = true;
            CornerRadius = 4;
            BorderThickness = 2;
            BorderColour = OsuColour.TextMuted;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var box = new Box { RelativeSizeAxes = Axes.Both };
            colour.BindValueChanged(c => box.Colour = c.NewValue, true);
            Child = box;
        }

        public Popover GetPopover()
        {
            // The picker auto-sizes its height; only its width may be set.
            var picker = new BasicHSVColourPicker { Width = 280 };
            picker.Current.BindTo(colour);
            return new BasicPopover { Child = picker };
        }

        protected override bool OnClick(ClickEvent e)
        {
            this.ShowPopover();
            return true;
        }
    }

    /// <summary>Small button that restores a colour to its default value.</summary>
    public partial class ResetButton : ClickableContainer
    {
        private Box background = null!;

        public ResetButton(Action action)
        {
            Action = action;
            Size = new Vector2(58, 26);
            Masking = true;
            CornerRadius = 4;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Reset",
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 13, weight: "SemiBold"),
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(OsuColour.BackgroundRaised, 100);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(OsuColour.Surface, 100);
            base.OnHoverLost(e);
        }
    }

    /// <summary>Button that rebinds a keyboard shortcut: click, then press the desired key.</summary>
    public partial class KeyRebindButton : ClickableContainer
    {
        private readonly Bindable<Key> key;
        private Box background = null!;
        private SpriteText text = null!;
        private bool listening;

        public KeyRebindButton(Bindable<Key> key)
        {
            this.key = key;
            Size = new Vector2(150, 30);
            Masking = true;
            CornerRadius = 4;
        }

        public override bool AcceptsFocus => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Surface },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 15, weight: "SemiBold"),
                },
            };

            key.BindValueChanged(_ => updateText(), true);
        }

        private void updateText() => text.Text = listening ? "Press a key..." : key.Value.ToString();

        protected override bool OnClick(ClickEvent e)
        {
            listening = true;
            background.Colour = OsuColour.Pink;
            updateText();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!listening)
                return false;

            if (e.Key != Key.Escape)
                key.Value = e.Key;

            stopListening();
            return true;
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            stopListening();
            base.OnFocusLost(e);
        }

        private void stopListening()
        {
            listening = false;
            background.Colour = OsuColour.Surface;
            updateText();
        }
    }
}
