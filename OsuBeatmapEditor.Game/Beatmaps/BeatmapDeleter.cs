using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Logging;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Deletes maps directly in osu!lazer's realm - the one operation that can't go through the .osz
    /// importer. Unlike the rest of the app (which only ever reads a copy of the database), this writes to
    /// the live <c>client.realm</c>. That is safe while lazer is running because Realm is multi-process, but
    /// to be cautious we back the database up before the first write of a session.
    ///
    /// "Delete set" sets <c>DeletePending</c> (osu!lazer's own soft delete, which both lazer and our reader
    /// honour); "Delete difficulty" removes the single beatmap and its .osu file usage from the set.
    /// </summary>
    public static class BeatmapDeleter
    {
        private static bool backedUpThisSession;

        /// <summary>Soft-deletes the whole set (DeletePending), exactly as osu!lazer does. Returns null on success, else an error message.</summary>
        public static string? DeleteSet(BeatmapSetModel set) => mutate(set, (realm, beatmapSet) =>
        {
            beatmapSet.DeletePending = true;
        });

        /// <summary>Removes a single difficulty (and its .osu file) from the set. Returns null on success, else an error message.</summary>
        public static string? DeleteDifficulty(BeatmapSetModel set, BeatmapDifficultyModel difficulty) => mutate(set, (realm, beatmapSet) =>
        {
            string hash = difficulty.OsuFileHash;
            if (hash.Length == 0)
                return;

            // The beatmap (top-level object) to remove, found by its .osu content hash.
            dynamic? target = null;
            foreach (dynamic b in beatmapSet.Beatmaps)
            {
                if (b.Hash is string h && h == hash)
                {
                    target = b;
                    break;
                }
            }

            if (target == null)
                return;

            // Its .osu file usage (an embedded object in the set's Files list), so no orphan remains.
            dynamic? fileUsage = null;
            foreach (dynamic u in beatmapSet.Files)
            {
                if (u.File?.Hash is string fh && fh == hash)
                {
                    fileUsage = u;
                    break;
                }
            }

            beatmapSet.Beatmaps.Remove(target);
            realm.Remove((IRealmObjectBase)target);

            // Embedded objects are deleted by removing them from their parent list.
            if (fileUsage != null)
                beatmapSet.Files.Remove(fileUsage);
        });

        /// <summary>
        /// Opens the live realm, locates the set by any of its difficulties' .osu hashes, then runs
        /// <paramref name="action"/> inside a write transaction.
        /// </summary>
        private static string? mutate(BeatmapSetModel set, Action<Realm, dynamic> action)
        {
            string? realmFile = LazerStorage.FindRealmFile();
            if (realmFile == null)
                return "osu!lazer's client.realm could not be located.";

            try
            {
                backup(realmFile);

                var config = new RealmConfiguration(realmFile) { IsDynamic = true };
                using var realm = Realm.GetInstance(config);

                var hashes = new HashSet<string>(set.Difficulties.Select(d => d.OsuFileHash).Where(h => h.Length > 0));
                if (hashes.Count == 0)
                    return "This set has no saved difficulties to match in the realm.";

                dynamic? match = null;
                foreach (dynamic s in realm.DynamicApi.All("BeatmapSet"))
                {
                    if (s.DeletePending == true)
                        continue;

                    foreach (dynamic b in s.Beatmaps)
                    {
                        if (b.Hash is string h && hashes.Contains(h))
                        {
                            match = s;
                            break;
                        }
                    }

                    if (match != null)
                        break;
                }

                if (match == null)
                    return "Couldn't find the matching set in osu!lazer's realm.";

                // Block-bodied lambda (not expression-bodied): `action` returns void, but `match` is dynamic,
                // so an expression body binds to Write<T>(Func<T>) and throws "can't convert void to object".
                realm.Write(() =>
                {
                    action(realm, match);
                });
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapDeleter: realm write failed");
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>Copies client.realm into an editor-backups folder once per session, before any write.</summary>
        private static void backup(string realmFile)
        {
            if (backedUpThisSession)
                return;

            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(realmFile)!, "editor-backups");
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, $"client-{DateTime.Now:yyyyMMdd-HHmmss}.realm");
                File.Copy(realmFile, dest, overwrite: false);
                backedUpThisSession = true;
            }
            catch
            {
                // Non-fatal: a missing backup shouldn't block the delete the user asked for.
            }
        }
    }
}
