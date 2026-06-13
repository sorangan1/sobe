using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Realms;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Shared helpers for the operations that write osu!lazer's content store + realm directly
    /// (<see cref="BeatmapRealmWriter"/> in-place save, <see cref="BeatmapRealmCreator"/> creation):
    /// content-addressed file writes, the SHA-256/MD5 hashes lazer keys off, the set-hash computation that
    /// matches lazer's importer, and locating a set in the realm by its difficulties' .osu hashes.
    /// </summary>
    internal static class LazerRealmFiles
    {
        /// <summary>Lower-case hex SHA-256 of the given bytes (lazer's <c>Beatmap.Hash</c> / file-store key).</summary>
        public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        /// <summary>Lower-case hex MD5 of the given bytes (lazer's <c>Beatmap.MD5Hash</c>).</summary>
        public static string Md5Hex(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

        /// <summary>Lower-case hex SHA-256 of a file's contents, streamed (for large files like audio).</summary>
        public static string Sha256HexFile(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        /// <summary>Writes bytes into the store at files/h0/h0h1/h, if not already present.</summary>
        public static void WriteToStore(string dataDir, string sha, byte[] bytes)
        {
            string path = EnsureStorePath(dataDir, sha);
            if (!File.Exists(path))
                File.WriteAllBytes(path, bytes);
        }

        /// <summary>Copies a source file into the store at files/h0/h0h1/h, if not already present.</summary>
        public static void CopyToStore(string dataDir, string sha, string sourcePath)
        {
            string path = EnsureStorePath(dataDir, sha);
            if (!File.Exists(path))
                File.Copy(sourcePath, path);
        }

        private static string EnsureStorePath(string dataDir, string sha)
        {
            string dir = Path.Combine(dataDir, "files", sha[..1], sha[..2]);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, sha);
        }

        /// <summary>
        /// SHA-256 over the concatenated content of every .osu in the set, ordered by filename - matching
        /// <c>RealmArchiveModelImporter.ComputeHash</c> with <c>HashableFileTypes = { ".osu" }</c>. Falls
        /// back to <paramref name="fallback"/> when no .osu files resolve.
        /// </summary>
        public static string ComputeSetHash(dynamic targetSet, string dataDir, string fallback)
        {
            var osuFiles = new List<(string filename, string hash)>();
            foreach (dynamic u in targetSet.Files)
            {
                string filename = u.Filename ?? string.Empty;
                string hash = u.File?.Hash ?? string.Empty;
                if (filename.EndsWith(".osu", StringComparison.OrdinalIgnoreCase) && hash.Length > 0)
                    osuFiles.Add((filename, hash));
            }

            using var hashable = new MemoryStream();
            foreach (var (_, hash) in osuFiles.OrderBy(f => f.filename))
            {
                string? path = LazerFileStore.ResolvePath(dataDir, hash);
                if (path == null)
                    continue;

                using var stream = File.OpenRead(path);
                stream.CopyTo(hashable);
            }

            if (hashable.Length == 0)
                return fallback;

            return Convert.ToHexString(SHA256.HashData(hashable.ToArray())).ToLowerInvariant();
        }

        /// <summary>
        /// Locates (or creates) the <c>File</c> realm object for a content hash. RealmFile maps to the realm
        /// type "File" with primary key Hash (ppy/osu Models/RealmFile.cs).
        /// </summary>
        public static dynamic ResolveOrCreateFile(Realm realm, string sha)
        {
            dynamic? file = realm.DynamicApi.Find("File", sha);
            return file ?? realm.DynamicApi.CreateObject("File", sha);
        }

        /// <summary>Replaces filename-illegal characters with '_' (mirrors lazer's GetValidFilename for our purposes).</summary>
        public static string ValidFilename(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "beatmap" : cleaned;
        }

        /// <summary>Finds a non-deleted BeatmapSet whose any difficulty's .osu hash is in <paramref name="hashes"/>.</summary>
        public static dynamic? FindSet(Realm realm, ISet<string> hashes)
        {
            if (hashes.Count == 0)
                return null;

            foreach (dynamic s in realm.DynamicApi.All("BeatmapSet"))
            {
                if (s.DeletePending == true)
                    continue;

                foreach (dynamic b in s.Beatmaps)
                {
                    if (b.Hash is string h && hashes.Contains(h))
                        return s;
                }
            }

            return null;
        }
    }
}
