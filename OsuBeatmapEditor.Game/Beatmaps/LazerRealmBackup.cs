using System;
using System.IO;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Backs osu!lazer's <c>client.realm</c> up once per editor session, before the first live write.
    /// Shared by every operation that mutates the live realm (delete via <see cref="BeatmapDeleter"/>,
    /// in-place save via <see cref="BeatmapRealmWriter"/>) so the database is copied at most once.
    /// </summary>
    public static class LazerRealmBackup
    {
        private static bool backedUpThisSession;

        /// <summary>Copies <paramref name="realmFile"/> into an editor-backups folder once per session. Best-effort.</summary>
        public static void EnsureBackup(string realmFile)
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
                // Non-fatal: a missing backup shouldn't block the operation the user asked for.
            }
        }
    }
}
