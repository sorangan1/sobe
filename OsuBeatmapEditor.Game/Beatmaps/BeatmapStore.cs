using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Reads beatmap sets from an osu!lazer <c>client.realm</c> database.
    ///
    /// The realm is opened in dynamic mode, which reads the schema straight from the file —
    /// so we never have to mirror osu!lazer's (frequently changing) model definitions, and no
    /// migration is triggered. We always work on a copy of the database to guarantee we can
    /// never modify or corrupt the user's osu!lazer installation.
    /// </summary>
    public static class BeatmapStore
    {
        private const string osu_ruleset_short_name = "osu";

        /// <summary>
        /// Loads all osu! Standard beatmap sets, or an empty list if osu!lazer cannot be found.
        /// Blocking; call from a background thread (e.g. a <c>[BackgroundDependencyLoader]</c>).
        /// </summary>
        /// <summary>Reads a dynamic realm date field defensively (the schema may omit it).</summary>
        private static DateTimeOffset? tryDate(Func<object?> getter)
        {
            try
            {
                return getter() is DateTimeOffset d ? d : (DateTimeOffset?)null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads a dynamic realm integer field defensively (the schema may omit it).</summary>
        private static int? tryInt(Func<object?> getter)
        {
            try
            {
                return getter() is { } value ? Convert.ToInt32(value) : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        public static IReadOnlyList<BeatmapSetModel> LoadAll()
        {
            string? dataDir = LazerStorage.FindDataDirectory();
            string? realmFile = dataDir == null ? null : Path.Combine(dataDir, "client.realm");
            if (dataDir == null || realmFile == null)
                return Array.Empty<BeatmapSetModel>();

            string tempCopy = Path.Combine(Path.GetTempPath(), $"osu-editor-{Guid.NewGuid():N}.realm");

            try
            {
                File.Copy(realmFile, tempCopy, overwrite: true);

                var config = new RealmConfiguration(tempCopy) { IsDynamic = true };
                using var realm = Realm.GetInstance(config);

                var result = new List<BeatmapSetModel>();

                foreach (dynamic set in realm.DynamicApi.All("BeatmapSet"))
                {
                    if (set.DeletePending == true)
                        continue;

                    var difficulties = new List<BeatmapDifficultyModel>();
                    string title = string.Empty, artist = string.Empty, author = string.Empty;
                    DateTimeOffset dateModified = DateTimeOffset.MinValue;

                    foreach (dynamic beatmap in set.Beatmaps)
                    {
                        string ruleset = beatmap.Ruleset?.ShortName ?? string.Empty;
                        if (ruleset != osu_ruleset_short_name)
                            continue;

                        dynamic? metadata = beatmap.Metadata;
                        title = metadata?.Title ?? title;
                        artist = metadata?.Artist ?? artist;
                        author = metadata?.Author?.Username ?? author;

                        DateTimeOffset? lastUpdate = tryDate(() => beatmap.LastLocalUpdate);
                        if (lastUpdate is { } lu && lu > dateModified)
                            dateModified = lu;

                        difficulties.Add(new BeatmapDifficultyModel
                        {
                            DifficultyName = beatmap.DifficultyName ?? string.Empty,
                            StarRating = beatmap.StarRating,
                            RulesetShortName = ruleset,
                            OsuFileHash = beatmap.Hash ?? string.Empty,
                            BackgroundFile = metadata?.BackgroundFile ?? string.Empty,
                            AudioFile = metadata?.AudioFile ?? string.Empty,
                            PreviewTime = tryInt(() => metadata?.PreviewTime) ?? -1,
                        });
                    }

                    // Skip sets that contain no osu! Standard difficulties (e.g. mania/taiko-only).
                    if (difficulties.Count == 0)
                        continue;

                    // Map each stored filename to its content hash (so we can locate audio, etc.).
                    var files = new Dictionary<string, string>();
                    foreach (dynamic usage in set.Files)
                    {
                        string filename = usage.Filename ?? string.Empty;
                        string hash = usage.File?.Hash ?? string.Empty;
                        if (filename.Length > 0 && hash.Length > 0)
                            files[filename.ToLowerInvariant()] = hash;
                    }

                    DateTimeOffset dateAdded = tryDate(() => set.DateAdded) ?? DateTimeOffset.MinValue;
                    if (dateModified == DateTimeOffset.MinValue)
                        dateModified = dateAdded;

                    result.Add(new BeatmapSetModel
                    {
                        OnlineID = (int)set.OnlineID,
                        Title = title,
                        Artist = artist,
                        Author = author,
                        Difficulties = difficulties,
                        DataDirectory = dataDir,
                        Files = files,
                        SearchText = $"{artist} {title} {author}".ToLowerInvariant(),
                        DateAdded = dateAdded,
                        DateModified = dateModified,
                    });
                }

                return result;
            }
            catch (Exception)
            {
                // A locked/incompatible realm shouldn't crash the editor; just show no maps.
                return Array.Empty<BeatmapSetModel>();
            }
            finally
            {
                try
                {
                    if (File.Exists(tempCopy))
                        File.Delete(tempCopy);
                }
                catch
                {
                    // Best-effort cleanup of the temp copy.
                }
            }
        }
    }
}
