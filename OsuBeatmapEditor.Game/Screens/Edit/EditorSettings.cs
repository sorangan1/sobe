using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// User-customisable editor settings (colours + keyboard shortcuts). Persisted to the app storage
    /// and saved automatically on change, so customisations are kept across sessions.
    /// </summary>
    public class EditorSettings
    {
        private const string filename = "editor-settings.json";

        public readonly Bindable<Colour4> UninheritedColour = new Bindable<Colour4>(Colour4.FromHex("ff3b3b"));
        public readonly Bindable<Colour4> InheritedColour = new Bindable<Colour4>(Colour4.FromHex("36d36e"));
        public readonly Bindable<Colour4> BookmarkColour = new Bindable<Colour4>(Colour4.FromHex("3399ff"));
        public readonly Bindable<Colour4> PreviewPointColour = new Bindable<Colour4>(Colour4.FromHex("ffd23f"));

        /// <summary>Kiai band colour. Defaults to the inherited colour; always rendered at 40% opacity.</summary>
        public readonly Bindable<Colour4> KiaiColour = new Bindable<Colour4>(Colour4.FromHex("36d36e"));

        // Top-timeline beat-grid lines: a colour per line type, and a height (px) for each tier.
        public readonly Bindable<Colour4> MeasureLineColour = new Bindable<Colour4>(Colour4.White);
        public readonly Bindable<Colour4> BeatLineColour = new Bindable<Colour4>(Colour4.White);
        public readonly Bindable<Colour4> HalfBeatLineColour = new Bindable<Colour4>(Colour4.FromHex("ff4d4d"));
        public readonly Bindable<Colour4> QuarterBeatLineColour = new Bindable<Colour4>(Colour4.FromHex("5b8cff"));

        public readonly BindableFloat MeasureLineHeight = new BindableFloat(22f) { MinValue = 2f, MaxValue = 40f, Precision = 1f };
        public readonly BindableFloat BeatLineHeight = new BindableFloat(16f) { MinValue = 2f, MaxValue = 40f, Precision = 1f };
        public readonly BindableFloat QuarterLineHeight = new BindableFloat(10f) { MinValue = 2f, MaxValue = 40f, Precision = 1f };

        /// <summary>Custom editor background colour, shown when the song background is toggled off.</summary>
        public readonly Bindable<Colour4> EditorBackgroundColour = new Bindable<Colour4>(Colour4.FromHex("1a1a2e"));

        /// <summary>Whether the editor shows the song's background image (true) or the custom colour (false).</summary>
        public readonly BindableBool UseSongBackground = new BindableBool(true);

        /// <summary>Dim applied over the song background, 0 (no dim) to 1 (fully black).</summary>
        public readonly BindableFloat BackgroundDim = new BindableFloat(0.55f) { MinValue = 0f, MaxValue = 1f, Precision = 0.05f };

        public readonly Bindable<Key> PlayPauseKey = new Bindable<Key>(Key.Space);
        public readonly Bindable<Key> ExitKey = new Bindable<Key>(Key.Escape);

        /// <summary>Default beatmap creator name, auto-filled into new/edited beatmaps.</summary>
        public readonly Bindable<string> DefaultCreator = new Bindable<string>(string.Empty);

        /// <summary>The settings section last opened, so the overlay reopens where the user left off.</summary>
        public readonly Bindable<string> LastSection = new Bindable<string>();

        private readonly Storage? storage;
        private readonly Dictionary<string, Bindable<Colour4>> colours;
        private readonly Dictionary<string, Bindable<Key>> keys;
        private readonly Dictionary<string, BindableFloat> floats;
        private bool loading;

        public EditorSettings(Storage? storage = null)
        {
            this.storage = storage;

            colours = new Dictionary<string, Bindable<Colour4>>
            {
                ["uninherited"] = UninheritedColour,
                ["inherited"] = InheritedColour,
                ["bookmark"] = BookmarkColour,
                ["preview"] = PreviewPointColour,
                ["kiai"] = KiaiColour,
                ["editorbg"] = EditorBackgroundColour,
                ["measureline"] = MeasureLineColour,
                ["beatline"] = BeatLineColour,
                ["halfbeatline"] = HalfBeatLineColour,
                ["quarterbeatline"] = QuarterBeatLineColour,
            };

            keys = new Dictionary<string, Bindable<Key>>
            {
                ["playpause"] = PlayPauseKey,
                ["exit"] = ExitKey,
            };

            floats = new Dictionary<string, BindableFloat>
            {
                ["measurelineheight"] = MeasureLineHeight,
                ["beatlineheight"] = BeatLineHeight,
                ["quarterlineheight"] = QuarterLineHeight,
            };

            load();

            foreach (var c in colours.Values)
                c.ValueChanged += _ => save();
            foreach (var k in keys.Values)
                k.ValueChanged += _ => save();
            foreach (var f in floats.Values)
                f.ValueChanged += _ => save();
            DefaultCreator.ValueChanged += _ => save();
            UseSongBackground.ValueChanged += _ => save();
            BackgroundDim.ValueChanged += _ => save();
        }

        private void load()
        {
            if (storage == null || !storage.Exists(filename))
                return;

            try
            {
                loading = true;
                using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                using var reader = new StreamReader(stream);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                if (data == null)
                    return;

                foreach (var (key, bindable) in colours)
                    if (data.TryGetValue("c_" + key, out string? hex) && Colour4.TryParseHex(hex, out var colour))
                        bindable.Value = colour;

                foreach (var (key, bindable) in keys)
                    if (data.TryGetValue("k_" + key, out string? name) && Enum.TryParse(name, out Key parsed))
                        bindable.Value = parsed;

                foreach (var (key, bindable) in floats)
                    if (data.TryGetValue("f_" + key, out string? raw) && float.TryParse(raw, out float value))
                        bindable.Value = value;

                if (data.TryGetValue("defaultCreator", out string? creator))
                    DefaultCreator.Value = creator;

                if (data.TryGetValue("useSongBackground", out string? useSong) && bool.TryParse(useSong, out bool useSongValue))
                    UseSongBackground.Value = useSongValue;

                if (data.TryGetValue("backgroundDim", out string? dim) && float.TryParse(dim, out float dimValue))
                    BackgroundDim.Value = dimValue;
            }
            catch
            {
                // A corrupt settings file shouldn't break the editor; fall back to defaults.
            }
            finally
            {
                loading = false;
            }
        }

        private void save()
        {
            if (storage == null || loading)
                return;

            try
            {
                var data = new Dictionary<string, string>();
                foreach (var (key, bindable) in colours)
                    data["c_" + key] = bindable.Value.ToHex();
                foreach (var (key, bindable) in keys)
                    data["k_" + key] = bindable.Value.ToString();
                foreach (var (key, bindable) in floats)
                    data["f_" + key] = bindable.Value.ToString();
                data["defaultCreator"] = DefaultCreator.Value;
                data["useSongBackground"] = UseSongBackground.Value.ToString();
                data["backgroundDim"] = BackgroundDim.Value.ToString();

                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(JsonSerializer.Serialize(data));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }
}
