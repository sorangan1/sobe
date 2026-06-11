using System;
using System.Collections.Generic;
using osu.Framework.Input.Events;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A keyboard shortcut: a main <see cref="Key"/> plus the modifier keys that must be held with it.
    /// Replaces the old bare-<c>Key</c> model so shortcuts can be real combinations (e.g. Ctrl+Shift+F).
    /// Serialised to/from a human string like <c>"Ctrl+Shift+F"</c>.
    /// </summary>
    public readonly record struct Shortcut(Key Key, bool Ctrl = false, bool Shift = false, bool Alt = false)
    {
        /// <summary>Keys that are modifiers themselves and so can never be the shortcut's main key.</summary>
        public static bool IsModifierKey(Key key) => key is
            Key.ControlLeft or Key.ControlRight
            or Key.ShiftLeft or Key.ShiftRight
            or Key.AltLeft or Key.AltRight
            or Key.WinLeft or Key.WinRight;

        /// <summary>True when this key event is exactly this shortcut (same main key AND the same modifier state).</summary>
        public bool Matches(KeyDownEvent e) =>
            e.Key == Key && e.ControlPressed == Ctrl && e.ShiftPressed == Shift && e.AltPressed == Alt;

        public override string ToString()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(KeyName(Key));
            return string.Join('+', parts);
        }

        public static bool TryParse(string? value, out Shortcut shortcut)
        {
            shortcut = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            bool ctrl = false, shift = false, alt = false;
            Key? key = null;

            foreach (string raw in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "ctrl": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    default:
                        if (Enum.TryParse(raw, out Key parsed))
                            key = parsed;
                        else
                            return false;
                        break;
                }
            }

            if (key == null)
                return false;

            shortcut = new Shortcut(key.Value, ctrl, shift, alt);
            return true;
        }

        /// <summary>Friendly name for a key (digits/letters without the osuTK <c>Number</c>/<c>Keypad</c> noise).</summary>
        private static string KeyName(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
                return key.ToString();
            if (key >= Key.Number0 && key <= Key.Number9)
                return ((int)(key - Key.Number0)).ToString();

            return key.ToString();
        }
    }
}
