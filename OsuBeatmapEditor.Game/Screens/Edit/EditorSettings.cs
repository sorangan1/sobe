using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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

        // Bumped when a stored default needs a one-time migration (see load()). v1: 0.06 circle border -> 0.07
        // (legacy Default skin). v2: stale 0.06/0.07 border -> ~0.0345 to match the modern Argon skin.
        private const int settings_version = 2;

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

        // The four combo colours cycled per new combo (map content). Editable so the user can recolour objects.
        public readonly Bindable<Colour4> ComboColour1 = new Bindable<Colour4>(Colour4.FromHex("FF7FA3"));
        public readonly Bindable<Colour4> ComboColour2 = new Bindable<Colour4>(Colour4.FromHex("66C7FF"));
        public readonly Bindable<Colour4> ComboColour3 = new Bindable<Colour4>(Colour4.FromHex("7FE3A0"));
        public readonly Bindable<Colour4> ComboColour4 = new Bindable<Colour4>(Colour4.FromHex("FFCB6B"));

        private readonly Bindable<Colour4>[] comboColours;

        /// <summary>The editable combo-colour palette, in cycle order.</summary>
        public IReadOnlyList<Bindable<Colour4>> ComboColours => comboColours;

        /// <summary>The combo colour for the given combo index (wraps around the palette).</summary>
        public Colour4 ComboColourFor(int comboIndex)
        {
            int i = ((comboIndex % comboColours.Length) + comboColours.Length) % comboColours.Length;
            return comboColours[i].Value;
        }

        /// <summary>Opacity of hit-object fills/bodies (0 = transparent, 1 = opaque). Defaults to 0.85.</summary>
        public readonly BindableFloat ObjectBackgroundOpacity = new BindableFloat(0.85f) { MinValue = 0f, MaxValue = 1f, Precision = 0.05f };

        /// <summary>Hit-object outline thickness, as a fraction of the circle diameter (drives both the
        /// hit-circle ring and the slider-body rim). Defaults to 0.06; the ring is drawn inside the radius.</summary>
        public readonly BindableFloat ObjectBorderThickness = new BindableFloat(0.06f) { MinValue = 0.01f, MaxValue = 0.15f, Precision = 0.01f };

        /// <summary>Slider-tick dot size, as a fraction of the circle diameter. Defaults to 0.10.</summary>
        public readonly BindableFloat SliderTickSize = new BindableFloat(0.10f) { MinValue = 0.04f, MaxValue = 0.3f, Precision = 0.01f };

        /// <summary>How long (ms) already-played objects linger and fade out in the editor. Defaults to 600.</summary>
        public readonly BindableFloat ObjectFadeOut = new BindableFloat(600f) { MinValue = 100f, MaxValue = 2000f, Precision = 50f };

        /// <summary>
        /// Audio offset (ms) applied to the on-screen playhead/objects during playback, to compensate for output
        /// latency - notably Bluetooth headphones, which buffer ~100-300 ms. A negative value delays the visuals so
        /// what you see lines up with what you hear; 0 disables it. Only affects playback preview, never the timing
        /// of placed objects or hitsound feedback.
        /// </summary>
        public readonly BindableFloat AudioOffset = new BindableFloat(0f) { MinValue = -500f, MaxValue = 500f, Precision = 1f };

        /// <summary>
        /// Whether the beta-notice popup is shown when the editor opens. The user can opt out from the
        /// popup itself (and back in via this setting).
        /// </summary>
        public readonly BindableBool ShowBetaPopup = new BindableBool(true);

        /// <summary>
        /// Whether hit objects are rendered with the beatmap's own combo colours (its <c>[Colours]</c>, or
        /// the default skin palette when it has none) - true - or with the editor's custom palette
        /// (<see cref="ComboColours"/>) - false.
        /// </summary>
        public readonly BindableBool UseMapColours = new BindableBool(true);

        /// <summary>Dim applied over the song background, 0 (no dim) to 1 (fully black).</summary>
        public readonly BindableFloat BackgroundDim = new BindableFloat(0.55f) { MinValue = 0f, MaxValue = 1f, Precision = 0.05f };

        /// <summary>Whether the app checks for and installs updates automatically on launch.</summary>
        public readonly BindableBool AutoUpdate = new BindableBool(true);

        /// <summary>
        /// Power-saving mode: caps the frame rate to the monitor's refresh rate (VSync) instead of the
        /// default 2x-refresh. Roughly halves GPU/CPU draw work on high-refresh displays, at the cost of a
        /// little input latency while scrubbing. Applied to the framework's global frame-sync setting.
        /// </summary>
        public readonly BindableBool PowerSaving = new BindableBool(false);

        /// <summary>Whether the one-time "enable automatic updates?" prompt has been answered.</summary>
        public readonly BindableBool AutoUpdatePrompted = new BindableBool(false);

        public readonly Bindable<Shortcut> PlayPauseKey = new Bindable<Shortcut>(new Shortcut(Key.Space));
        public readonly Bindable<Shortcut> ExitKey = new Bindable<Shortcut>(new Shortcut(Key.Escape));

        /// <summary>Opens the timing-points editor.</summary>
        public readonly Bindable<Shortcut> TimingPointsKey = new Bindable<Shortcut>(new Shortcut(Key.F6));

        /// <summary>Opens the song setup (metadata/difficulty) editor.</summary>
        public readonly Bindable<Shortcut> SongSetupKey = new Bindable<Shortcut>(new Shortcut(Key.F5));

        /// <summary>Opens the editor settings dialog.</summary>
        public readonly Bindable<Shortcut> SettingsKey = new Bindable<Shortcut>(new Shortcut(Key.O));

        /// <summary>Toggles the expanded hitsound-lanes editor (Clap/Whistle/Finish) in the top timeline.</summary>
        public readonly Bindable<Shortcut> HitsoundsKey = new Bindable<Shortcut>(new Shortcut(Key.H));

        /// <summary>Toggles distance snapping (placement spaced from the previous object by time, like lazer).</summary>
        public readonly Bindable<Shortcut> DistanceSnapKey = new Bindable<Shortcut>(new Shortcut(Key.Y));

        /// <summary>Converts the selected slider into a stream of circles at the current beat-snap divisor.</summary>
        public readonly Bindable<Shortcut> ConvertStreamKey = new Bindable<Shortcut>(new Shortcut(Key.F, Ctrl: true, Shift: true));

        /// <summary>Toggles the editor's Modding Mode (discussion bubbles + filters/messages panels).</summary>
        public readonly Bindable<Shortcut> ModdingModeKey = new Bindable<Shortcut>(new Shortcut(Key.M, Ctrl: true, Shift: true));

        /// <summary>Toggles the Pattern Gallery (saved selections).</summary>
        public readonly Bindable<Shortcut> PatternGalleryKey = new Bindable<Shortcut>(new Shortcut(Key.P));

        /// <summary>Adds an uninherited (red/BPM) timing point at the playhead.</summary>
        public readonly Bindable<Shortcut> AddBpmPointKey = new Bindable<Shortcut>(new Shortcut(Key.P, Ctrl: true));

        /// <summary>Adds an inherited (green/SV) timing point at the playhead.</summary>
        public readonly Bindable<Shortcut> AddSvPointKey = new Bindable<Shortcut>(new Shortcut(Key.P, Ctrl: true, Shift: true));

        // Auto-preview cursor settings (persisted; the AU chip's mini-menu edits these).
        public readonly Bindable<Colour4> AutoCursorColour = new Bindable<Colour4>(Colour4.FromHex("ffdb33"));
        public readonly BindableFloat AutoTrailLength = new BindableFloat(10f) { MinValue = 0f, MaxValue = 120f, Precision = 1f };
        public readonly BindableFloat AutoTrailWidth = new BindableFloat(1f) { MinValue = 0.2f, MaxValue = 4f, Precision = 0.1f };

        /// <summary>Auto-preview: show the K1/K2 key overlay (the cursor's "tapping"), like osu!lazer.</summary>
        public readonly BindableBool AutoKeyOverlay = new BindableBool(false);

        /// <summary>Auto-preview: "humanise" the cursor (arcs, overshoot, jitter, aim error) instead of perfect Auto.</summary>
        public readonly BindableBool AutoHumanize = new BindableBool(false);

        /// <summary>Modding Mode: discussion types the user has hidden (comma-separated), persisted across maps.</summary>
        public readonly Bindable<string> ModdingMutedTypes = new Bindable<string>(string.Empty);

        /// <summary>Default beatmap creator name, auto-filled into new/edited beatmaps.</summary>
        public readonly Bindable<string> DefaultCreator = new Bindable<string>(string.Empty);

        /// <summary>The settings section last opened, so the overlay reopens where the user left off.</summary>
        public readonly Bindable<string> LastSection = new Bindable<string>();

        private readonly Storage? storage;
        private readonly Dictionary<string, Bindable<Colour4>> colours;
        private readonly Dictionary<string, Bindable<Shortcut>> keys;
        private readonly Dictionary<string, BindableFloat> floats;
        private bool loading;
        private bool resaveAfterLoad;

        public EditorSettings(Storage? storage = null)
        {
            this.storage = storage;

            comboColours = new[] { ComboColour1, ComboColour2, ComboColour3, ComboColour4 };

            colours = new Dictionary<string, Bindable<Colour4>>
            {
                ["uninherited"] = UninheritedColour,
                ["inherited"] = InheritedColour,
                ["bookmark"] = BookmarkColour,
                ["preview"] = PreviewPointColour,
                ["kiai"] = KiaiColour,
                ["measureline"] = MeasureLineColour,
                ["beatline"] = BeatLineColour,
                ["halfbeatline"] = HalfBeatLineColour,
                ["quarterbeatline"] = QuarterBeatLineColour,
                ["combo1"] = ComboColour1,
                ["combo2"] = ComboColour2,
                ["combo3"] = ComboColour3,
                ["combo4"] = ComboColour4,
                ["autocursor"] = AutoCursorColour,
            };

            keys = new Dictionary<string, Bindable<Shortcut>>
            {
                ["playpause"] = PlayPauseKey,
                ["exit"] = ExitKey,
                ["timingpoints"] = TimingPointsKey,
                ["songsetup"] = SongSetupKey,
                ["settings"] = SettingsKey,
                ["hitsounds"] = HitsoundsKey,
                ["distancesnap"] = DistanceSnapKey,
                ["convertstream"] = ConvertStreamKey,
                ["moddingmode"] = ModdingModeKey,
                ["patterngallery"] = PatternGalleryKey,
                ["addbpmpoint"] = AddBpmPointKey,
                ["addsvpoint"] = AddSvPointKey,
            };

            floats = new Dictionary<string, BindableFloat>
            {
                ["measurelineheight"] = MeasureLineHeight,
                ["beatlineheight"] = BeatLineHeight,
                ["quarterlineheight"] = QuarterLineHeight,
                ["objectopacity"] = ObjectBackgroundOpacity,
                ["objectborder"] = ObjectBorderThickness,
                ["slidertick"] = SliderTickSize,
                ["objectfadeout"] = ObjectFadeOut,
                ["audiooffset"] = AudioOffset,
                ["autotraillength"] = AutoTrailLength,
                ["autotrailwidth"] = AutoTrailWidth,
            };

            load();

            foreach (var c in colours.Values)
                c.ValueChanged += _ => save();
            foreach (var k in keys.Values)
                k.ValueChanged += _ => save();
            foreach (var f in floats.Values)
                f.ValueChanged += _ => save();
            DefaultCreator.ValueChanged += _ => save();
            ModdingMutedTypes.ValueChanged += _ => save();
            ShowBetaPopup.ValueChanged += _ => save();
            UseMapColours.ValueChanged += _ => save();
            BackgroundDim.ValueChanged += _ => save();
            AutoUpdate.ValueChanged += _ => save();
            AutoUpdatePrompted.ValueChanged += _ => save();
            PowerSaving.ValueChanged += _ => save();
            AutoKeyOverlay.ValueChanged += _ => save();
            AutoHumanize.ValueChanged += _ => save();

            // Persist any one-time migration applied during load() (and the bumped version) once, now that
            // the loading guard is clear.
            if (resaveAfterLoad)
                save();
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
                    if (data.TryGetValue("k_" + key, out string? name) && Shortcut.TryParse(name, out var parsed))
                        bindable.Value = parsed;

                foreach (var (key, bindable) in floats)
                    if (data.TryGetValue("f_" + key, out string? raw) && float.TryParse(raw, out float value))
                        bindable.Value = value;

                if (data.TryGetValue("defaultCreator", out string? creator))
                    DefaultCreator.Value = creator;

                if (data.TryGetValue("moddingMutedTypes", out string? muted))
                    ModdingMutedTypes.Value = muted;

                if (data.TryGetValue("showBetaPopup", out string? showBeta) && bool.TryParse(showBeta, out bool showBetaValue))
                    ShowBetaPopup.Value = showBetaValue;

                if (data.TryGetValue("useMapColours", out string? useMap) && bool.TryParse(useMap, out bool useMapValue))
                    UseMapColours.Value = useMapValue;

                if (data.TryGetValue("backgroundDim", out string? dim) && float.TryParse(dim, out float dimValue))
                    BackgroundDim.Value = dimValue;

                if (data.TryGetValue("autoUpdate", out string? autoUp) && bool.TryParse(autoUp, out bool autoUpValue))
                    AutoUpdate.Value = autoUpValue;

                if (data.TryGetValue("autoUpdatePrompted", out string? prompted) && bool.TryParse(prompted, out bool promptedValue))
                    AutoUpdatePrompted.Value = promptedValue;

                if (data.TryGetValue("powerSaving", out string? power) && bool.TryParse(power, out bool powerValue))
                    PowerSaving.Value = powerValue;

                if (data.TryGetValue("autoKeyOverlay", out string? keyOverlay) && bool.TryParse(keyOverlay, out bool keyOverlayValue))
                    AutoKeyOverlay.Value = keyOverlayValue;

                if (data.TryGetValue("autoHumanize", out string? humanize) && bool.TryParse(humanize, out bool humanizeValue))
                    AutoHumanize.Value = humanizeValue;

                // Humanize tuning: apply any stored per-field overrides onto the live HumanizeTuning statics.
                foreach (var f in humanizeTuningFields())
                    if (data.TryGetValue("hz." + f.Name, out string? hv) &&
                        float.TryParse(hv, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                        f.SetValue(null, fv);

                int version = data.TryGetValue("version", out string? vRaw) && int.TryParse(vRaw, out int v) ? v : 0;
                migrate(version);
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

        /// <summary>
        /// Applies one-time fixes to settings loaded from an older file version, then flags a resave so the
        /// migration (and the bumped <see cref="settings_version"/>) persists.
        /// </summary>
        private void migrate(int fromVersion)
        {
            if (fromVersion >= settings_version)
                return;

            // The border default has changed twice; a stored copy of either old default (0.06, then 0.07) is
            // a value the user never deliberately chose, so bump it to the current Argon-matching default.
            // A deliberately different value is left as-is.
            bool isStaleBorderDefault = Math.Abs(ObjectBorderThickness.Value - 0.06f) < 0.0001f
                                        || Math.Abs(ObjectBorderThickness.Value - 0.07f) < 0.0001f;
            if (fromVersion < settings_version && isStaleBorderDefault)
                ObjectBorderThickness.Value = ObjectBorderThickness.Default;

            resaveAfterLoad = true;
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
                data["moddingMutedTypes"] = ModdingMutedTypes.Value;
                data["showBetaPopup"] = ShowBetaPopup.Value.ToString();
                data["useMapColours"] = UseMapColours.Value.ToString();
                data["backgroundDim"] = BackgroundDim.Value.ToString();
                data["autoUpdate"] = AutoUpdate.Value.ToString();
                data["autoUpdatePrompted"] = AutoUpdatePrompted.Value.ToString();
                data["powerSaving"] = PowerSaving.Value.ToString();
                data["autoKeyOverlay"] = AutoKeyOverlay.Value.ToString();
                data["autoHumanize"] = AutoHumanize.Value.ToString();

                // Humanize tuning: persist every HumanizeTuning static field so dialled-in values survive a restart.
                foreach (var f in humanizeTuningFields())
                    data["hz." + f.Name] = ((float)f.GetValue(null)!).ToString(CultureInfo.InvariantCulture);

                data["version"] = settings_version.ToString();

                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(JsonSerializer.Serialize(data));
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        /// <summary>Persists the current <see cref="HumanizeTuning"/> values (the live tuning panel calls this on "Save").</summary>
        public void SaveHumanizeTuning() => save();

        /// <summary>The public static float fields of <see cref="HumanizeTuning"/> (all its tunables).</summary>
        private static IEnumerable<FieldInfo> humanizeTuningFields()
        {
            foreach (var f in typeof(HumanizeTuning).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(float))
                    yield return f;
        }
    }
}
