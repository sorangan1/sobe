using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
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

            // Two columns: the label fills the remaining width (truncating with an ellipsis if it's too long),
            // the control auto-sizes on the right. This keeps a long label from sliding under the control or
            // spilling past the row edge, whatever width the control turns out to be.
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = height,
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(GridSizeMode.AutoSize),
                    },
                    Content = new[]
                    {
                        new[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Right = EditorTheme.Spacing.Md },
                                Child = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    RelativeSizeAxes = Axes.X,
                                    Truncate = true,
                                    Text = label,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Body(),
                                },
                            },
                            control,
                        },
                    },
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

        protected override bool CanAddCharacter(char character)
            => char.IsDigit(character) || character == '.' || (character == '-' && bindable.MinValue < 0);

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
    /// A <see cref="ResetButton"/> bound to a setting: it restores the bindable's default on click and is
    /// only visible while the value differs from that default (so an unchanged field shows no reset button).
    /// While hidden it collapses out of its layout flow (Alpha 0 → not present).
    /// </summary>
    public partial class BindableResetButton<T> : ResetButton
    {
        private readonly Bindable<T> bindable;

        public BindableResetButton(Bindable<T> bindable)
            : base(bindable.SetDefault)
        {
            this.bindable = bindable;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void loadVisibility()
        {
            bindable.BindValueChanged(_ =>
                this.FadeTo(bindable.IsDefault ? 0 : 1, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease), true);
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
                shortcut.Value = new Shortcut(e.Key, Shortcut.CommandPressed(e), e.ShiftPressed, e.AltPressed);

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

    /// <summary>
    /// Dropdown that selects the application's audio output device, bound to the framework's
    /// <see cref="AudioManager.AudioDevice"/> (an empty entry means "system default"). The framework
    /// persists the choice in its own config, and the list refreshes live as devices are plugged in or
    /// removed. Mirrors osu!lazer's AudioDevicesSettings.
    /// </summary>
    public partial class AudioDeviceSetting : CompositeDrawable
    {
        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private AudioDeviceDropdown dropdown = null!;

        public AudioDeviceSetting()
        {
            Width = 320;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = dropdown = new AudioDeviceDropdown { RelativeSizeAxes = Axes.X };

            // Populate before binding so the persisted device is a valid selection in the list.
            updateItems();
            dropdown.Current.BindTo(audio.AudioDevice);

            audio.OnNewDevice += onDeviceChanged;
            audio.OnLostDevice += onDeviceChanged;
        }

        // Device hotplug events can arrive off the update thread; marshal back before touching drawables.
        private void onDeviceChanged(string _) => Schedule(updateItems);

        private void updateItems()
        {
            var items = new List<string> { string.Empty };
            items.AddRange(audio.AudioDeviceNames);

            // Keep the currently selected device listed even if it's momentarily gone (e.g. unplugged).
            string preferred = audio.AudioDevice.Value;
            if (items.All(i => i != preferred))
                items.Add(preferred);

            dropdown.Items = items.Where(i => i != null).Distinct().ToList();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (audio != null)
            {
                audio.OnNewDevice -= onDeviceChanged;
                audio.OnLostDevice -= onDeviceChanged;
            }
        }

        /// <summary>Renders the empty "system default" entry as a friendly label.</summary>
        private partial class AudioDeviceDropdown : ThemedDropdown<string>
        {
            protected override LocalisableString GenerateItemText(string item)
                => string.IsNullOrEmpty(item) ? "Default" : item;
        }
    }

    /// <summary>A compact themed on/off switch two-way bound to a <see cref="BindableBool"/>.</summary>
    public partial class ToggleSwitch : ClickableContainer
    {
        private readonly BindableBool current;
        private Box track = null!;
        private Container knob = null!;

        public ToggleSwitch(BindableBool current)
        {
            this.current = current;
            Size = new Vector2(46, 24);
            Action = () => current.Value = !current.Value;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 12,
                    Child = track = new Box { RelativeSizeAxes = Axes.Both },
                },
                knob = new Container
                {
                    Size = new Vector2(18),
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Masking = true,
                    CornerRadius = 9,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Text },
                },
            };

            current.BindValueChanged(v => update(v.NewValue, animate: true), true);
        }

        private void update(bool on, bool animate)
        {
            double d = animate ? EditorTheme.Motion.Fast : 0;
            track.FadeColour(on ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, d, EditorTheme.Motion.Ease);
            knob.MoveToX(on ? Width - knob.Width - 3 : 3, d, EditorTheme.Motion.Ease);
        }
    }
}
