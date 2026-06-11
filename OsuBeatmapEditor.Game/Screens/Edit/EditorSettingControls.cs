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
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Body(),
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
            Size = new Vector2(64, EditorTheme.Sizing.InputHeight);
            Masking = true;
            CornerRadius = EditorTheme.Radius.Sm;
            BorderThickness = EditorTheme.Sizing.BorderThickness;
            BorderColour = EditorTheme.Colours.Border;
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
            Size = new Vector2(58, EditorTheme.Sizing.InputHeight);
            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Reset",
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.Label(),
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            base.OnHoverLost(e);
        }
    }

    /// <summary>
    /// Button that rebinds a keyboard shortcut: click, then press the desired combination. Modifier keys
    /// (Ctrl/Shift/Alt) held while pressing a normal key are captured as part of the shortcut - pressing a
    /// modifier alone just waits for the main key. Escape cancels without changing the binding.
    /// </summary>
    public partial class KeyRebindButton : ClickableContainer
    {
        private readonly Bindable<Shortcut> shortcut;
        private Box background = null!;
        private SpriteText text = null!;
        private bool listening;

        public KeyRebindButton(Bindable<Shortcut> shortcut)
        {
            this.shortcut = shortcut;
            Size = new Vector2(150, EditorTheme.Sizing.RowHeight);
            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
        }

        public override bool AcceptsFocus => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.BodyStrong(),
                },
            };

            shortcut.BindValueChanged(_ => updateText(), true);
        }

        private void updateText()
        {
            text.Text = listening ? "Press keys..." : shortcut.Value.ToString();
            text.Colour = listening ? EditorTheme.Colours.TextMuted : EditorTheme.Colours.Text;
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Listening state: a soft accent tint + accent border - NOT the solid accent fill, which is
            // reserved for primary/active controls (design system: the accent isn't a generic highlight).
            listening = true;
            background.Colour = EditorTheme.Colours.AccentSoft;
            BorderThickness = EditorTheme.Sizing.BorderThickness;
            BorderColour = EditorTheme.Colours.Accent;
            updateText();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!listening)
                return false;

            // Wait for a non-modifier key, then capture it together with whatever modifiers are held.
            if (Shortcut.IsModifierKey(e.Key))
                return true;

            if (e.Key != Key.Escape)
                shortcut.Value = new Shortcut(e.Key, e.ControlPressed, e.ShiftPressed, e.AltPressed);

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
            background.Colour = EditorTheme.Colours.Control;
            BorderThickness = 0;
            updateText();
        }
    }
}
