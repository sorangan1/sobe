using System;
using System.IO;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Locates an existing osu!lazer data directory on the current platform.
    /// This standalone editor has no install of its own to read from, so it borrows
    /// osu!lazer's storage (the realm database + content-addressable file store).
    /// </summary>
    public static class LazerStorage
    {
        /// <summary>
        /// Resolves the osu!lazer data directory, or <c>null</c> if none can be found.
        /// </summary>
        public static string? FindDataDirectory()
        {
            foreach (var candidate in candidatePaths())
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(Path.Combine(candidate, "client.realm")))
                    return candidate;
            }

            return null;
        }

        /// <summary>Full path to the realm database, or <c>null</c> if osu!lazer was not found.</summary>
        public static string? FindRealmFile()
        {
            var dir = FindDataDirectory();
            return dir == null ? null : Path.Combine(dir, "client.realm");
        }

        private static string[] candidatePaths()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (OperatingSystem.IsMacOS())
            {
                return new[]
                {
                    Path.Combine(home, "Library", "Application Support", "osu"),
                    Path.Combine(home, ".local", "share", "osu"),
                };
            }

            if (OperatingSystem.IsWindows())
            {
                return new[]
                {
                    Path.Combine(appData, "osu"),
                };
            }

            // Linux and others.
            return new[]
            {
                Path.Combine(home, ".local", "share", "osu"),
            };
        }
    }
}
