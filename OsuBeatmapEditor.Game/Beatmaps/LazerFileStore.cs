using System.IO;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Resolves files in osu!lazer's content-addressable store, where a file with SHA-256 hash
    /// <c>h</c> lives at <c>&lt;dataDir&gt;/files/&lt;h[0]&gt;/&lt;h[0..2]&gt;/&lt;h&gt;</c>.
    /// </summary>
    public static class LazerFileStore
    {
        /// <summary>Returns the on-disk path for a stored hash, or <c>null</c> if it can't be formed/found.</summary>
        public static string? ResolvePath(string dataDirectory, string hash)
        {
            if (string.IsNullOrEmpty(dataDirectory) || string.IsNullOrEmpty(hash) || hash.Length < 2)
                return null;

            string path = Path.Combine(dataDirectory, "files", hash[..1], hash[..2], hash);
            return File.Exists(path) ? path : null;
        }
    }
}
