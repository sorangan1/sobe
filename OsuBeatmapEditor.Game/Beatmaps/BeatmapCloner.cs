using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Derives new maps from an existing one by text-patching its .osu (so nothing is lost), packaging
    /// the result into a .osz and handing it to osu!lazer to import - the same round-trip as
    /// <see cref="BeatmapSaver"/>. Used by the carousel's "Create new Difficulty" action and to seed
    /// "Create new Set" with the source's audio and timing.
    /// </summary>
    public static class BeatmapCloner
    {
        /// <summary>
        /// Adds a new (empty) difficulty to <paramref name="set"/> using <paramref name="template"/> as the
        /// base: timing points, metadata, difficulty settings, audio and background are copied verbatim, but
        /// the hit objects start empty and the version is renamed (matching osu!lazer's "create new difficulty").
        /// Repackages the whole set plus the new .osu and imports it. Returns false if the template can't be
        /// resolved or the import couldn't be launched.
        /// </summary>
        public static bool CreateDifficulty(BeatmapSetModel set, BeatmapDifficultyModel template, string difficultyName)
        {
            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, template.OsuFileHash);
            if (osuPath == null)
                return false;

            string[] lines = File.ReadAllLines(osuPath);
            string newOsu = cloneWithEmptyHitObjects(lines, difficultyName);

            var (artist, title, creator) = readMetadata(lines);
            string newFileName = uniqueName(set.Files.Keys, sanitise($"{artist} - {title} ({creator}) [{difficultyName}].osu"));

            string exportDir = Path.Combine(Path.GetTempPath(), "osu-editor-exports");
            Directory.CreateDirectory(exportDir);

            string oszPath = Path.Combine(exportDir, sanitise($"{artist} - {title} ({creator}).osz"));
            if (File.Exists(oszPath))
                File.Delete(oszPath);

            using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Create))
            {
                // Every original file verbatim (keeps the existing difficulties intact)...
                foreach (var (name, hash) in set.Files)
                {
                    string? path = LazerFileStore.ResolvePath(set.DataDirectory, hash);
                    if (path != null)
                        archive.CreateEntryFromFile(path, name, CompressionLevel.NoCompression);
                }

                // ...plus the new difficulty.
                var entry = archive.CreateEntry(newFileName, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(newOsu);
            }

            return LazerImporter.Import(oszPath);
        }

        /// <summary>The audio filename from a .osu's [General] section (lower-cased), or empty if absent.</summary>
        public static string ExtractAudioFilename(IReadOnlyList<string> lines)
        {
            string section = string.Empty;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    continue;
                }

                if (section == "General" && key(raw) == "AudioFilename")
                    return value(raw).Trim();
            }

            return string.Empty;
        }

        /// <summary>The raw [TimingPoints] lines from a .osu (red + green), in file order.</summary>
        public static List<string> ExtractTimingPointLines(IReadOnlyList<string> lines)
        {
            var result = new List<string>();
            string section = string.Empty;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    continue;
                }

                if (section == "TimingPoints" && trimmed.Length > 0)
                    result.Add(trimmed);
            }

            return result;
        }

        /// <summary>Copies a .osu verbatim, but renames the version, zeroes the online id and empties the hit objects.</summary>
        private static string cloneWithEmptyHitObjects(string[] lines, string difficultyName)
        {
            var sb = new StringBuilder();
            string section = string.Empty;

            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    sb.Append(raw).Append('\n');
                    continue;
                }

                // Drop the original hit objects - the new difficulty starts blank.
                if (section == "HitObjects")
                    continue;

                if (section == "Metadata")
                {
                    switch (key(raw))
                    {
                        case "Version":
                            sb.Append($"Version:{difficultyName}").Append('\n');
                            continue;

                        // Force a fresh online identity so lazer treats this as a new difficulty.
                        case "BeatmapID":
                            sb.Append("BeatmapID:0").Append('\n');
                            continue;
                    }
                }

                sb.Append(raw).Append('\n');
            }

            return sb.ToString();
        }

        private static (string artist, string title, string creator) readMetadata(string[] lines)
        {
            string section = string.Empty, artist = "", title = "", creator = "";
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    continue;
                }

                if (section != "Metadata")
                    continue;

                switch (key(raw))
                {
                    case "Artist": artist = value(raw); break;
                    case "Title": title = value(raw); break;
                    case "Creator": creator = value(raw); break;
                }
            }

            return (artist, title, creator);
        }

        private static string key(string line)
        {
            int sep = line.IndexOf(':');
            return sep < 0 ? string.Empty : line[..sep].Trim();
        }

        private static string value(string line)
        {
            int sep = line.IndexOf(':');
            return sep < 0 ? string.Empty : line[(sep + 1)..];
        }

        /// <summary>Ensures the chosen entry name doesn't collide with an existing file in the set.</summary>
        private static string uniqueName(IEnumerable<string> existing, string name)
        {
            var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(name))
                return name;

            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                string candidate = $"{stem} ({i}){ext}";
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private static string sanitise(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "beatmap.osz" : cleaned;
        }
    }
}
