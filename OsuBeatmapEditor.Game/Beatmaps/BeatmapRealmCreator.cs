using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Logging;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Creates new beatmaps directly in osu!lazer's live realm + content store, mirroring lazer's own
    /// <c>BeatmapManager.CreateNew</c> / <c>CreateNewDifficulty</c>. This replaces the old .osz-import path for
    /// creation, so nothing launches osu!lazer: the new set/difficulty appears in the realm immediately.
    ///
    /// Like <see cref="BeatmapRealmWriter"/> and <see cref="BeatmapDeleter"/> this writes the live database, so
    /// it backs it up once per session first (<see cref="LazerRealmBackup"/>) and does all realm mutation
    /// inside a single write transaction (which rolls back safely on any error).
    /// </summary>
    public static class BeatmapRealmCreator
    {
        // BeatmapOnlineStatus.LocallyModified - a locally authored map (never seen online).
        private const int locally_modified_status = -4;

        /// <summary>
        /// Adds a new (empty) difficulty cloned from <paramref name="template"/> to the existing set, writing
        /// it straight into lazer's realm. Returns <c>null</c> on success, otherwise an error message.
        /// </summary>
        public static string? CreateDifficulty(BeatmapSetModel set, BeatmapDifficultyModel template, string difficultyName,
            bool copyDifficultySettings = true, bool copyBpm = true, bool copySv = true)
        {
            if (template.OsuFileHash.Length == 0)
                return "The template difficulty has no saved .osu file.";

            string? templatePath = LazerFileStore.ResolvePath(set.DataDirectory, template.OsuFileHash);
            if (templatePath == null)
                return "The template difficulty's .osu file could not be located.";

            // Empty-hitobjects clone of the template, with the new difficulty name. Metadata/audio/background are
            // always kept; the toggles control whether difficulty settings and BPM/SV timing points are copied.
            string osuText = BeatmapCloner.BuildEmptyDifficultyOsu(File.ReadAllLines(templatePath), difficultyName,
                copyDifficultySettings, copyBpm, copySv);
            byte[] bytes = new UTF8Encoding(false).GetBytes(osuText);
            string sha = LazerRealmFiles.Sha256Hex(bytes);
            string md5 = LazerRealmFiles.Md5Hex(bytes);

            string filename = LazerRealmFiles.ValidFilename($"{set.Artist} - {set.Title} ({set.Author}) [{difficultyName}].osu");

            return mutate(set.DataDirectory, (realm, dataDir) =>
            {
                var hashes = new HashSet<string>(set.Difficulties.Select(d => d.OsuFileHash).Where(h => h.Length > 0));
                dynamic? targetSet = LazerRealmFiles.FindSet(realm, hashes);
                if (targetSet == null)
                    return "Couldn't find the set in osu!lazer's realm.";

                // The template beatmap object - its ruleset/metadata/difficulty seed the new one.
                dynamic? source = null;
                foreach (dynamic b in targetSet.Beatmaps)
                {
                    if (b.Hash is string h && h == template.OsuFileHash)
                    {
                        source = b;
                        break;
                    }
                }

                if (source == null)
                    return "The template difficulty was not found in the set.";

                LazerRealmFiles.WriteToStore(dataDir, sha, bytes);

                realm.Write(() =>
                {
                    dynamic beatmap = realm.DynamicApi.CreateObject("Beatmap", Guid.NewGuid());
                    beatmap.Ruleset = source.Ruleset; // shared osu! ruleset object
                    beatmap.Metadata = cloneMetadata(realm, source.Metadata);
                    copyDifficulty(realm, beatmap, source.Difficulty, copyDifficultySettings);
                    realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "UserSettings");

                    beatmap.DifficultyName = difficultyName;
                    beatmap.Hash = sha;
                    beatmap.MD5Hash = md5;
                    beatmap.OnlineID = -1;
                    beatmap.BeatDivisor = 4;
                    beatmap.StarRating = 0;
                    beatmap.Status = locally_modified_status;
                    beatmap.LastLocalUpdate = DateTimeOffset.Now;

                    targetSet.Beatmaps.Add(beatmap);
                    beatmap.BeatmapSet = targetSet;

                    addFileUsage(realm, targetSet, sha, filename);

                    targetSet.Hash = LazerRealmFiles.ComputeSetHash(targetSet, dataDir, sha);
                    targetSet.Status = locally_modified_status;
                });

                return null;
            });
        }

        /// <summary>
        /// Creates a brand-new set (one empty difficulty) directly in lazer's realm from a
        /// <see cref="NewBeatmapRequest"/>. Only uninherited (BPM) timing points are kept and no bookmarks are
        /// written. Returns <c>null</c> on success, otherwise an error message.
        /// </summary>
        public static string? CreateSet(NewBeatmapRequest request)
        {
            if (!request.IsValid)
                return "The new beatmap request is incomplete.";

            string audioFilename = BeatmapArchiveWriter.ResolveAudioFilename(request);
            string osuFilename = BeatmapArchiveWriter.OsuFilename(request);

            string osuText = BeatmapArchiveWriter.BuildOsuText(request, audioFilename, uninheritedTimingOnly: true);
            byte[] osuBytes = new UTF8Encoding(false).GetBytes(osuText);
            string osuSha = LazerRealmFiles.Sha256Hex(osuBytes);
            string osuMd5 = LazerRealmFiles.Md5Hex(osuBytes);

            string audioSha = LazerRealmFiles.Sha256HexFile(request.AudioPath);

            return mutate(LazerStorage.FindDataDirectory(), (realm, dataDir) =>
            {
                dynamic? ruleset = realm.DynamicApi.Find("Ruleset", "osu");
                if (ruleset == null)
                    return "The osu! ruleset is missing from lazer's realm.";

                // Stage both files in the content store before referencing them from the realm.
                LazerRealmFiles.CopyToStore(dataDir, audioSha, request.AudioPath);
                LazerRealmFiles.WriteToStore(dataDir, osuSha, osuBytes);

                realm.Write(() =>
                {
                    dynamic set = realm.DynamicApi.CreateObject("BeatmapSet", Guid.NewGuid());
                    set.DateAdded = DateTimeOffset.Now;
                    set.OnlineID = -1;
                    set.DeletePending = false;
                    set.Status = locally_modified_status;

                    dynamic metadata = cloneNewMetadata(realm, request, audioFilename);

                    dynamic beatmap = realm.DynamicApi.CreateObject("Beatmap", Guid.NewGuid());
                    beatmap.Ruleset = ruleset;
                    beatmap.Metadata = metadata;

                    // Difficulty settings match BeatmapArchiveWriter's generated [Difficulty] block.
                    dynamic difficulty = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "Difficulty");
                    difficulty.DrainRate = 5f;
                    difficulty.CircleSize = 4f;
                    difficulty.OverallDifficulty = 5f;
                    difficulty.ApproachRate = 5f;
                    difficulty.SliderMultiplier = 1.4;
                    difficulty.SliderTickRate = 1.0;

                    realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "UserSettings");

                    beatmap.DifficultyName = request.DifficultyName;
                    beatmap.Hash = osuSha;
                    beatmap.MD5Hash = osuMd5;
                    beatmap.OnlineID = -1;
                    beatmap.BeatDivisor = 4;
                    beatmap.StarRating = 0;
                    beatmap.Status = locally_modified_status;
                    beatmap.LastLocalUpdate = DateTimeOffset.Now;

                    set.Beatmaps.Add(beatmap);
                    beatmap.BeatmapSet = set;

                    addFileUsage(realm, set, audioSha, audioFilename);
                    addFileUsage(realm, set, osuSha, osuFilename);

                    set.Hash = LazerRealmFiles.ComputeSetHash(set, dataDir, osuSha);
                });

                return null;
            });
        }

        /// <summary>
        /// Bootstraps ("clones") a collab into lazer's realm from its full .osu text plus the audio and optional
        /// background bytes (downloaded from the collab server). Creates a one-difficulty set whose .osu is the
        /// collab content verbatim, so the collaborator can open and edit it. Returns null on success, else an error.
        /// </summary>
        public static string? BootstrapCollab(string osuText, byte[] audioBytes, string audioFilename, byte[]? backgroundBytes, string? backgroundFilename)
        {
            if (string.IsNullOrEmpty(osuText))
                return "The collab has no map content yet.";
            if (audioBytes.Length == 0)
                return "The collab is missing its audio.";

            ParsedBeatmap parsed = OsuFileDecoder.Decode(new StringReader(osuText));

            // The .osu references these by name; keep them in step so the bootstrapped set resolves its files.
            string audioName = !string.IsNullOrEmpty(parsed.AudioFilename) ? parsed.AudioFilename : audioFilename;
            string? bgName = !string.IsNullOrEmpty(parsed.BackgroundFilename) ? parsed.BackgroundFilename : backgroundFilename;

            byte[] osuBytes = new UTF8Encoding(false).GetBytes(osuText);
            string osuSha = LazerRealmFiles.Sha256Hex(osuBytes);
            string osuMd5 = LazerRealmFiles.Md5Hex(osuBytes);
            string audioSha = LazerRealmFiles.Sha256Hex(audioBytes);
            string? bgSha = backgroundBytes is { Length: > 0 } ? LazerRealmFiles.Sha256Hex(backgroundBytes) : null;

            string osuFilename = LazerRealmFiles.ValidFilename($"{parsed.Artist} - {parsed.Title} ({parsed.Creator}) [{parsed.Version}].osu");

            return mutate(LazerStorage.FindDataDirectory(), (realm, dataDir) =>
            {
                dynamic? ruleset = realm.DynamicApi.Find("Ruleset", "osu");
                if (ruleset == null)
                    return "The osu! ruleset is missing from lazer's realm.";

                // Stage every file in the content store before referencing it from the realm.
                LazerRealmFiles.WriteToStore(dataDir, osuSha, osuBytes);
                LazerRealmFiles.WriteToStore(dataDir, audioSha, audioBytes);
                if (bgSha != null && backgroundBytes != null)
                    LazerRealmFiles.WriteToStore(dataDir, bgSha, backgroundBytes);

                realm.Write(() =>
                {
                    dynamic set = realm.DynamicApi.CreateObject("BeatmapSet", Guid.NewGuid());
                    set.DateAdded = DateTimeOffset.Now;
                    set.OnlineID = -1;
                    set.DeletePending = false;
                    set.Status = locally_modified_status;

                    dynamic metadata = realm.DynamicApi.CreateObject("BeatmapMetadata"); // no primary key
                    metadata.Title = parsed.Title;
                    metadata.TitleUnicode = string.IsNullOrEmpty(parsed.TitleUnicode) ? parsed.Title : parsed.TitleUnicode;
                    metadata.Artist = parsed.Artist;
                    metadata.ArtistUnicode = string.IsNullOrEmpty(parsed.ArtistUnicode) ? parsed.Artist : parsed.ArtistUnicode;
                    metadata.Source = parsed.Source;
                    metadata.Tags = parsed.Tags;
                    metadata.PreviewTime = parsed.PreviewTime;
                    metadata.AudioFile = audioName;
                    metadata.BackgroundFile = bgName ?? string.Empty;
                    dynamic author = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)metadata, "Author");
                    author.Username = parsed.Creator;

                    dynamic beatmap = realm.DynamicApi.CreateObject("Beatmap", Guid.NewGuid());
                    beatmap.Ruleset = ruleset;
                    beatmap.Metadata = metadata;

                    dynamic difficulty = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "Difficulty");
                    difficulty.DrainRate = parsed.HpDrainRate;
                    difficulty.CircleSize = parsed.CircleSize;
                    difficulty.OverallDifficulty = parsed.OverallDifficulty;
                    difficulty.ApproachRate = parsed.EffectiveApproachRate;
                    difficulty.SliderMultiplier = (double)parsed.SliderMultiplier;
                    difficulty.SliderTickRate = (double)parsed.SliderTickRate;

                    realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "UserSettings");

                    beatmap.DifficultyName = parsed.Version;
                    beatmap.Hash = osuSha;
                    beatmap.MD5Hash = osuMd5;
                    beatmap.OnlineID = -1;
                    beatmap.BeatDivisor = 4;
                    beatmap.StarRating = 0;
                    beatmap.Status = locally_modified_status;
                    beatmap.LastLocalUpdate = DateTimeOffset.Now;

                    set.Beatmaps.Add(beatmap);
                    beatmap.BeatmapSet = set;

                    addFileUsage(realm, set, audioSha, audioName);
                    if (bgSha != null && !string.IsNullOrEmpty(bgName))
                        addFileUsage(realm, set, bgSha, bgName);
                    addFileUsage(realm, set, osuSha, osuFilename);

                    set.Hash = LazerRealmFiles.ComputeSetHash(set, dataDir, osuSha);
                });

                return null;
            });
        }

        /// <summary>Creates a fresh BeatmapMetadata for a new set, with a RealmUser author.</summary>
        private static dynamic cloneNewMetadata(Realm realm, NewBeatmapRequest request, string audioFilename)
        {
            dynamic meta = realm.DynamicApi.CreateObject("BeatmapMetadata"); // no primary key
            meta.Title = request.Title;
            meta.TitleUnicode = request.Title;
            meta.Artist = request.Artist;
            meta.ArtistUnicode = request.Artist;
            meta.Source = string.Empty;
            meta.Tags = string.Empty;
            meta.PreviewTime = -1;
            meta.AudioFile = audioFilename;
            meta.BackgroundFile = string.Empty;

            dynamic author = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)meta, "Author");
            author.Username = request.Creator;
            return meta;
        }

        /// <summary>Deep-copies a beatmap's metadata into a new BeatmapMetadata (so the new diff owns its own).</summary>
        private static dynamic cloneMetadata(Realm realm, dynamic source)
        {
            dynamic meta = realm.DynamicApi.CreateObject("BeatmapMetadata"); // no primary key
            meta.Title = source.Title;
            meta.TitleUnicode = source.TitleUnicode;
            meta.Artist = source.Artist;
            meta.ArtistUnicode = source.ArtistUnicode;
            meta.Source = source.Source;
            meta.Tags = source.Tags;
            meta.PreviewTime = source.PreviewTime;
            meta.AudioFile = source.AudioFile;
            meta.BackgroundFile = source.BackgroundFile;

            dynamic author = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)meta, "Author");
            if (source.Author != null)
            {
                author.Username = source.Author.Username;
                author.OnlineID = source.Author.OnlineID;
            }

            return meta;
        }

        /// <summary>Copies the source beatmap's difficulty settings into the new beatmap's embedded Difficulty.</summary>
        private static void copyDifficulty(Realm realm, dynamic beatmap, dynamic source, bool copySettings = true)
        {
            dynamic difficulty = realm.DynamicApi.CreateEmbeddedObjectForProperty((IRealmObjectBase)beatmap, "Difficulty");
            if (source == null)
                return;

            // When the user opts out of copying difficulty settings, fall back to osu!'s neutral defaults for
            // HP/CS/OD/AR (matching the reset applied to the .osu text). Slider multiplier/tick are SV-related
            // and always carried over so velocity stays consistent with the copied timing.
            if (copySettings)
            {
                difficulty.DrainRate = source.DrainRate;
                difficulty.CircleSize = source.CircleSize;
                difficulty.OverallDifficulty = source.OverallDifficulty;
                difficulty.ApproachRate = source.ApproachRate;
            }
            else
            {
                difficulty.DrainRate = 5f;
                difficulty.CircleSize = 5f;
                difficulty.OverallDifficulty = 5f;
                difficulty.ApproachRate = 5f;
            }

            difficulty.SliderMultiplier = source.SliderMultiplier;
            difficulty.SliderTickRate = source.SliderTickRate;
        }

        /// <summary>Adds a RealmNamedFileUsage (embedded) to the set's Files, pointing at the File for the hash.</summary>
        private static void addFileUsage(Realm realm, dynamic set, string sha, string filename)
        {
            dynamic usage = realm.DynamicApi.AddEmbeddedObjectToList((System.Collections.IList)set.Files);
            usage.File = LazerRealmFiles.ResolveOrCreateFile(realm, sha);
            usage.Filename = filename;
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

                string? result = action(realm, dataDir);
                if (result == null)
                    BeatmapStore.InvalidateCache();
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapRealmCreator: realm write failed");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
