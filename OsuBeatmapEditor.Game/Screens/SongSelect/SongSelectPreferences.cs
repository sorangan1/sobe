using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using osu.Framework.Bindables;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Persisted song-select preferences (currently the last-used sort order), saved to app storage
    /// and reloaded on the next launch. Mirrors the JSON pattern of
    /// <see cref="OsuBeatmapEditor.Game.Screens.Edit.EditorSettings"/>.
    /// </summary>
    public class SongSelectPreferences
    {
        private const string filename = "song-select-prefs.json";

        /// <summary>The last "sort by" the user picked in the carousel.</summary>
        public readonly Bindable<SortMode> Sort = new Bindable<SortMode>(SortMode.Artist);

        private readonly Storage? storage;
        private bool loading;

        public SongSelectPreferences(Storage? storage = null)
        {
            this.storage = storage;
            load();
            Sort.ValueChanged += _ => save();
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

                if (data.TryGetValue("sort", out string? sortRaw) && Enum.TryParse(sortRaw, out SortMode parsed))
                    Sort.Value = parsed;
            }
            catch
            {
                // A corrupt prefs file shouldn't break the screen; fall back to defaults.
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
                var data = new Dictionary<string, string> { ["sort"] = Sort.Value.ToString() };
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
