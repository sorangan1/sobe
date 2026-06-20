using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using osu.Framework.Logging;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Imports an arbitrary beatmap (a <c>.osz</c> archive or an extracted folder) straight into osu!lazer's
    /// live realm + content store, so the editor never has to hand the file to osu!lazer to import. This
    /// generalises <see cref="BeatmapRealmCreator.BootstrapCollab"/> to many difficulties and an arbitrary set of
    /// files (audio, backgrounds, hitsounds, storyboard, video, ...).
    ///
    /// Like the other direct-realm writers it backs the database up once per session
    /// (<see cref="LazerRealmBackup"/>) and does every mutation inside one write transaction, which rolls back
    /// safely on any error.
    /// </summary>
    public static class BeatmapArchiveImporter
    {
        // BeatmapOnlineStatus.LocallyModified - mirrors how BeatmapRealmCreator files locally authored maps.
        private const int locally_modified_status = -4;

        /// <summary>The result of an import: a friendly title on success, or an error message.</summary>
        public readonly record struct Result(bool Success, string Message)
        {
            public static Result Ok(string title) => new Result(true, title);
            public static Result Fail(string error) => new Result(false, error);
        }

        /// <summary>Imports a <c>.osz</c> archive. The archive's entries become the set's files verbatim.</summary>
        public static Result ImportOsz(string oszPath)
        {
            if (!File.Exists(oszPath))
                return Result.Fail("The .osz file could not be found.");

            List<(string filename, byte[] bytes)> files;
            try
            {
                files = new List<(string, byte[])>();
                using var archive = ZipFile.OpenRead(oszPath);
                foreach (var entry in archive.Entries)
                {
                    // Skip directory entries (zero name) - we only carry real files.
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    using var stream = entry.Open();
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    files.Add((normaliseName(entry.FullName), ms.ToArray()));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapArchiveImporter: failed to read .osz");
                return Result.Fail("The .osz archive could not be read (is it a valid osu! beatmap?).");
            }

            return Import(files);
        }

        /// <summary>Imports an extracted beatmap folder, using each file's path relative to the folder as its name.</summary>
        public static Result ImportFolder(string dir)
        {
            if (!Directory.Exists(dir))
                return Result.Fail("The folder could not be found.");

            List<(string filename, byte[] bytes)> files;
            try
            {
                files = new List<(string, byte[])>();
                foreach (string path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(dir, path);
                    files.Add((normaliseName(rel), File.ReadAllBytes(path)));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapArchiveImporter: failed to read folder");
                return Result.Fail("The beatmap folder could not be read.");
            }

            return Import(files);
        }

        /// <summary>
        /// Imports a flat list of (filename, bytes). Creates one <c>BeatmapSet</c> with a <c>Beatmap</c> per
        /// osu!-Standard <c>.osu</c> file and a file usage for every file. Returns the result.
        /// </summary>
        public static Result Import(IReadOnlyList<(string filename, byte[] bytes)> files)
        {
            if (files.Count == 0)
                return Result.Fail("The beatmap is empty.");

            // Parse every .osu, keeping only osu! Standard (Mode 0) difficulties - the editor is Standard-only,
            // matching BeatmapStore's filter. Non-.osu files (and non-standard .osu) are still carried as files.
            var difficulties = new List<ParsedDifficulty>();
            foreach (var (filename, bytes) in files)
            {
                if (!filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                try
                {
                    text = System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    continue;
                }

                if (readIntKey(text, "Mode") is int mode && mode != 0)
                    continue; // taiko/catch/mania difficulty - skip the row but keep the file.

                ParsedBeatmap parsed;
                try
                {
                    parsed = OsuFileDecoder.Decode(new StringReader(text));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"BeatmapArchiveImporter: failed to parse {filename}");
                    continue;
                }

                difficulties.Add(new ParsedDifficulty(filename, LazerRealmFiles.Sha256Hex(bytes), LazerRealmFiles.Md5Hex(bytes), parsed));
            }

            if (difficulties.Count == 0)
                return Result.Fail("No osu! Standard difficulties were found in this beatmap.");

            string displayTitle = $"{difficulties[0].Parsed.Artist} - {difficulties[0].Parsed.Title}";

            string? error = mutate(LazerStorage.FindDataDirectory(), (realm, dataDir) =>
            {
                // Dedup: if any of these .osu files is already in the (non-deleted) library, treat as a no-op.
                var diffHashes = new HashSet<string>(difficulties.Select(d => d.Sha));
                if (LazerRealmFiles.FindSet(realm, diffHashes) != null)
                    return "already-in-library";

                dynamic? ruleset = realm.DynamicApi.Find("Ruleset", "osu");
                if (ruleset == null)
                    return "The osu! ruleset is missing from lazer's realm.";

                // Stage every file in the content store, keyed by its SHA, before referencing it from the realm.
                var fileHashes = new List<(string filename, string sha)>();
                foreach (var (filename, bytes) in files)
                {
                    string sha = LazerRealmFiles.Sha256Hex(bytes);
                    LazerRealmFiles.WriteToStore(dataDir, sha, bytes);
                    fileHashes.Add((filename, sha));
                }

                realm.Write(() =>
                {
                    dynamic set = realm.DynamicApi.CreateObject("BeatmapSet", Guid.NewGuid());
                    set.DateAdded = DateTimeOffset.Now;
                    set.OnlineID = -1;
                    set.DeletePending = false;
                    set.Status = locally_modified_status;

                    foreach (var diff in difficulties)
                    {
                        dynamic beatmap = realm.DynamicApi.CreateObject("Beatmap", Guid.NewGuid());
                        beatmap.Ruleset = ruleset;
                        beatmap.Metadata = buildMetadata(realm, diff.Parsed);

                        dynamic difficulty = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "Difficulty");
                        difficulty.DrainRate = diff.Parsed.HpDrainRate;
                        difficulty.CircleSize = diff.Parsed.CircleSize;
                        difficulty.OverallDifficulty = diff.Parsed.OverallDifficulty;
                        difficulty.ApproachRate = diff.Parsed.EffectiveApproachRate;
                        difficulty.SliderMultiplier = (double)diff.Parsed.SliderMultiplier;
                        difficulty.SliderTickRate = (double)diff.Parsed.SliderTickRate;

                        realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "UserSettings");

                        beatmap.DifficultyName = diff.Parsed.Version;
                        beatmap.Hash = diff.Sha;
                        beatmap.MD5Hash = diff.Md5;
                        beatmap.OnlineID = -1;
                        beatmap.BeatDivisor = 4;
                        beatmap.StarRating = 0;
                        beatmap.Status = locally_modified_status;
                        beatmap.LastLocalUpdate = DateTimeOffset.Now;

                        set.Beatmaps.Add(beatmap);
                        beatmap.BeatmapSet = set;
                    }

                    foreach (var (filename, sha) in fileHashes)
                        addFileUsage(realm, set, sha, filename);

                    set.Hash = LazerRealmFiles.ComputeSetHash(set, dataDir, difficulties[0].Sha);
                });

                return null;
            });

            if (error == "already-in-library")
                return Result.Fail($"\"{displayTitle}\" is already in your library.");

            return error == null ? Result.Ok(displayTitle) : Result.Fail(error);
        }

        private readonly record struct ParsedDifficulty(string Filename, string Sha, string Md5, ParsedBeatmap Parsed);

        /// <summary>Builds a fresh BeatmapMetadata + Author from a parsed difficulty (each diff owns its own).</summary>
        private static dynamic buildMetadata(Realm realm, ParsedBeatmap parsed)
        {
            dynamic meta = realm.DynamicApi.CreateObject("BeatmapMetadata"); // no primary key
            meta.Title = parsed.Title;
            meta.TitleUnicode = string.IsNullOrEmpty(parsed.TitleUnicode) ? parsed.Title : parsed.TitleUnicode;
            meta.Artist = parsed.Artist;
            meta.ArtistUnicode = string.IsNullOrEmpty(parsed.ArtistUnicode) ? parsed.Artist : parsed.ArtistUnicode;
            meta.Source = parsed.Source;
            meta.Tags = parsed.Tags;
            meta.PreviewTime = parsed.PreviewTime;
            meta.AudioFile = parsed.AudioFilename;
            meta.BackgroundFile = parsed.BackgroundFilename;

            dynamic author = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)meta, "Author");
            author.Username = parsed.Creator;
            return meta;
        }

        /// <summary>Adds a RealmNamedFileUsage (embedded) to the set's Files, pointing at the File for the hash.</summary>
        private static void addFileUsage(Realm realm, dynamic set, string sha, string filename)
        {
            dynamic usage = realm.DynamicApi.AddEmbeddedObjectToList((System.Collections.IList)set.Files);
            usage.File = LazerRealmFiles.ResolveOrCreateFile(realm, sha);
            usage.Filename = filename;
        }

        /// <summary>Normalises an archive/relative path to lazer's forward-slash convention.</summary>
        private static string normaliseName(string name) => name.Replace('\\', '/').TrimStart('/');

        /// <summary>Reads the first <c>Key: value</c> integer in the .osu text (e.g. General's Mode), or null.</summary>
        private static int? readIntKey(string text, string key)
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                int colon = line.IndexOf(':');
                if (colon < 0 || line[..colon].Trim().Length != key.Length)
                    continue;

                if (int.TryParse(line[(colon + 1)..].Trim(), out int value))
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Opens the live realm (dynamic), backs it up once per session, and runs <paramref name="action"/>
        /// (which owns its own write transaction). Returns the action's error string, or a caught exception.
        /// </summary>
        private static string? mutate(string? dataDir, Func<Realm, string, string?> action)
        {
            if (string.IsNullOrEmpty(dataDir))
                return "osu!lazer's data directory could not be located.";

            string realmFile = Path.Combine(dataDir, "client.realm");
            if (!File.Exists(realmFile))
                return "osu!lazer's client.realm could not be located.";

            try
            {
                LazerRealmBackup.EnsureBackup(realmFile);

                var config = new RealmConfiguration(realmFile) { IsDynamic = true };
                using var realm = Realm.GetInstance(config);

                return action(realm, dataDir);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapArchiveImporter: realm write failed");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
