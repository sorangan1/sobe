using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Logging;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Saves edits to an existing difficulty by writing osu!lazer's live <c>client.realm</c> and content
    /// store in place - exactly how lazer's own editor saves (<c>BeatmapManager.save</c>). This replaces the
    /// .osz-import path for edits, which produced a <em>duplicate</em> set: lazer re-hashes every imported
    /// archive, so an edited difficulty's new content hash made lazer file it as a separate set.
    ///
    /// Mirrors lazer step-for-step inside one write transaction: encode the patched .osu, store it under its
    /// SHA-256, re-point the difficulty's file usage at the new file (renaming to match metadata), update the
    /// difficulty's hashes/metadata/settings, then recompute the set hash the way lazer's importer does.
    /// Like <see cref="BeatmapDeleter"/> this is one of the few operations that writes the live realm; it
    /// backs the database up once per session first (<see cref="LazerRealmBackup"/>).
    /// </summary>
    public static class BeatmapRealmWriter
    {
        // BeatmapOnlineStatus.LocallyModified - the flag lazer sets on a map edited in its own editor.
        private const int locally_modified_status = -4;

        /// <summary>
        /// Writes the edited difficulty into osu!lazer's realm in place. Returns <c>null</c> on success,
        /// otherwise a human-readable error message.
        /// </summary>
        public static string? Save(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, BeatmapSaver.Edits edits)
        {
            string? patched = BeatmapSaver.BuildPatchedOsu(set, difficulty, parsed, edits);
            if (patched == null)
                return "The original .osu file could not be located.";

            return writeOsu(set, difficulty, new UTF8Encoding(false).GetBytes(patched), edits);
        }

        /// <summary>
        /// Overwrites an existing difficulty with the exact .osu text pulled from a collab (no re-encoding, so the
        /// content matches the collaborator's byte-for-byte). Metadata/difficulty fields are taken from the pulled
        /// text. Returns null on success, otherwise an error message.
        /// </summary>
        public static string? SaveRaw(BeatmapSetModel set, BeatmapDifficultyModel difficulty, string osuText)
        {
            if (string.IsNullOrEmpty(osuText))
                return "The pulled .osu is empty.";

            ParsedBeatmap parsed = OsuFileDecoder.Decode(new StringReader(osuText));
            return writeOsu(set, difficulty, new UTF8Encoding(false).GetBytes(osuText), editsFromParsed(parsed));
        }

        /// <summary>Snapshots a decoded beatmap's metadata/difficulty into the saver's edit set (for SaveRaw).</summary>
        private static BeatmapSaver.Edits editsFromParsed(ParsedBeatmap p) => new BeatmapSaver.Edits
        {
            Title = p.Title,
            TitleUnicode = string.IsNullOrEmpty(p.TitleUnicode) ? p.Title : p.TitleUnicode,
            Artist = p.Artist,
            ArtistUnicode = string.IsNullOrEmpty(p.ArtistUnicode) ? p.Artist : p.ArtistUnicode,
            Creator = p.Creator,
            Version = p.Version,
            Source = p.Source,
            Tags = p.Tags,
            Hp = p.HpDrainRate,
            Cs = p.CircleSize,
            Ar = p.EffectiveApproachRate,
            Od = p.OverallDifficulty,
            StackLeniency = p.StackLeniency,
            SliderMultiplier = p.SliderMultiplier,
            SliderTickRate = p.SliderTickRate,
            ComboColours = p.ComboColours.ToList(),
        };

        /// <summary>Stores the given .osu bytes for the difficulty and re-points its realm entry (shared by Save/SaveRaw).</summary>
        private static string? writeOsu(BeatmapSetModel set, BeatmapDifficultyModel difficulty, byte[] bytes, BeatmapSaver.Edits edits)
        {
            string oldHash = difficulty.OsuFileHash;
            if (string.IsNullOrEmpty(oldHash))
                return "This difficulty has no saved .osu file to update in place.";

            // The exact bytes we will store (UTF-8, no BOM) and the hashes lazer keys off.
            string newSha = LazerRealmFiles.Sha256Hex(bytes);
            string newMd5 = LazerRealmFiles.Md5Hex(bytes);

            // The filename lazer would give this difficulty - it renames the .osu to match the metadata on save.
            string targetFilename = LazerRealmFiles.ValidFilename($"{edits.Artist} - {edits.Title} ({edits.Creator}) [{edits.Version}].osu");

            string? realmFile = LazerStorage.FindRealmFile();
            if (realmFile == null)
                return "osu!lazer's client.realm could not be located.";

            string dataDir = Path.GetDirectoryName(realmFile)!;

            try
            {
                LazerRealmBackup.EnsureBackup(realmFile);

                // Put the new content in the store first; leaving the old file behind is a harmless orphan.
                LazerRealmFiles.WriteToStore(dataDir, newSha, bytes);

                var config = new RealmConfiguration(realmFile) { IsDynamic = true };
                using var realm = Realm.GetInstance(config);

                var hashes = new HashSet<string>(set.Difficulties.Select(d => d.OsuFileHash).Where(h => h.Length > 0));
                dynamic? targetSet = LazerRealmFiles.FindSet(realm, hashes);
                if (targetSet == null)
                    return "Couldn't find the matching set in osu!lazer's realm.";

                string? error = null;

                // Block-bodied lambda (see BeatmapDeleter): the dynamic call inside returns a value, so an
                // expression body would bind to Write<T>(Func<T>) and throw on the void/object mismatch.
                realm.Write(() =>
                {
                    error = applyEdit(realm, targetSet, oldHash, newSha, newMd5, targetFilename, edits, dataDir);
                });

                if (error == null)
                    BeatmapStore.InvalidateCache();
                return error;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapRealmWriter: realm write failed");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>Applies every in-place change for the edited difficulty inside an open write transaction.</summary>
        private static string? applyEdit(Realm realm, dynamic targetSet, string oldHash, string newSha, string newMd5, string targetFilename, BeatmapSaver.Edits e, string dataDir)
        {
            // The beatmap (difficulty) we edited, located by its current .osu content hash.
            dynamic? beatmap = null;
            foreach (dynamic b in targetSet.Beatmaps)
            {
                if (b.Hash is string h && h == oldHash)
                {
                    beatmap = b;
                    break;
                }
            }

            if (beatmap == null)
                return "The edited difficulty was not found in the set.";

            // Its .osu file usage (embedded in the set's Files list).
            dynamic? usage = null;
            foreach (dynamic u in targetSet.Files)
            {
                if (u.File?.Hash is string fh && fh == oldHash)
                {
                    usage = u;
                    break;
                }
            }

            if (usage == null)
                return "The edited difficulty's file entry was not found in the set.";

            // Resolve (or create) the RealmFile keyed by the new content hash.
            dynamic newFile = LazerRealmFiles.ResolveOrCreateFile(realm, newSha);

            // Re-point the file usage and rename it to match the (possibly changed) metadata, as lazer does.
            usage.File = newFile;
            usage.Filename = targetFilename;

            // Update the difficulty's own identity + flags so the carousel and lazer reflect the edit.
            beatmap.Hash = newSha;
            beatmap.MD5Hash = newMd5;
            beatmap.DifficultyName = e.Version;
            beatmap.LastLocalUpdate = DateTimeOffset.Now;
            beatmap.Status = locally_modified_status;

            // Persist our own star-rating computation (lazer would recompute on its next scan, but this keeps our
            // carousel correct immediately). -1 means the caller didn't compute one, so leave the stored value.
            if (e.StarRating >= 0)
                beatmap.StarRating = e.StarRating;

            dynamic difficulty = beatmap.Difficulty;
            if (difficulty != null)
            {
                difficulty.DrainRate = e.Hp;
                difficulty.CircleSize = e.Cs;
                difficulty.OverallDifficulty = e.Od;
                difficulty.ApproachRate = e.Ar;
                // Realm stores these as doubles; round-trip via the .osu text format so e.g. 1.4f -> 1.4.
                difficulty.SliderMultiplier = roundTrip(e.SliderMultiplier);
                difficulty.SliderTickRate = roundTrip(e.SliderTickRate);
            }

            dynamic metadata = beatmap.Metadata;
            if (metadata != null)
            {
                metadata.Title = e.Title;
                metadata.TitleUnicode = e.TitleUnicode;
                metadata.Artist = e.Artist;
                metadata.ArtistUnicode = e.ArtistUnicode;
                metadata.Source = e.Source;
                metadata.Tags = e.Tags;
                if (metadata.Author != null)
                    metadata.Author.Username = e.Creator;
            }

            // Recompute the set hash exactly as lazer's importer does (SHA-256 over the set's .osu file
            // contents, concatenated in filename order) so its change tracking stays consistent.
            targetSet.Hash = LazerRealmFiles.ComputeSetHash(targetSet, dataDir, newSha);
            targetSet.Status = locally_modified_status;

            return null;
        }

        /// <summary>Round-trips a float through the .osu "0.###" text form to the double lazer would decode.</summary>
        private static double roundTrip(float v) =>
            double.Parse(v.ToString("0.###", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
