using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Saves edits to an existing beatmap by text-patching the original .osu (so nothing else is lost),
    /// packaging it with its audio/background into a .osz, and handing it to osu!lazer to import.
    /// </summary>
    public static class BeatmapSaver
    {
        /// <summary>Holds the edited metadata/difficulty values to write back.</summary>
        public class Edits
        {
            public string Title = "", TitleUnicode = "", Artist = "", ArtistUnicode = "", Creator = "", Version = "", Source = "", Tags = "";
            public float Hp, Cs, Ar, Od, StackLeniency = 0.7f;
        }

        public static bool Save(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, Edits edits)
        {
            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);
            if (osuPath == null)
                return false;

            // Re-emit the [HitObjects] section from the (possibly edited) object list so deletions/edits
            // persist, while every other line is preserved verbatim.
            var hitObjectLines = parsed.HitObjects
                .Select(o => o.RawLine)
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            string patched = patch(File.ReadAllLines(osuPath), edits, hitObjectLines);

            // Which stored file is the difficulty we edited (so we replace its content, keep the rest).
            string? editedFile = null;
            foreach (var (name, hash) in set.Files)
            {
                if (string.Equals(hash, difficulty.OsuFileHash, StringComparison.OrdinalIgnoreCase))
                {
                    editedFile = name;
                    break;
                }
            }

            string exportDir = Path.Combine(Path.GetTempPath(), "osu-editor-exports");
            Directory.CreateDirectory(exportDir);

            string oszPath = Path.Combine(exportDir, sanitise($"{edits.Artist} - {edits.Title} ({edits.Creator}).osz"));
            if (File.Exists(oszPath))
                File.Delete(oszPath);

            using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Create))
            {
                // Repackage the WHOLE set: every original file verbatim, except the edited difficulty,
                // whose content we replace with the patched version. This keeps the other difficulties.
                foreach (var (name, hash) in set.Files)
                {
                    if (string.Equals(name, editedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                        writer.Write(patched);
                    }
                    else
                    {
                        string? path = LazerFileStore.ResolvePath(set.DataDirectory, hash);
                        if (path != null)
                            archive.CreateEntryFromFile(path, name, CompressionLevel.NoCompression);
                    }
                }

                // Fallback: if the edited difficulty wasn't in the file list, add it under a derived name.
                if (editedFile == null)
                {
                    string osuName = sanitise($"{edits.Artist} - {edits.Title} ({edits.Creator}) [{edits.Version}].osu");
                    var entry = archive.CreateEntry(osuName, CompressionLevel.NoCompression);
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(patched);
                }
            }

            return LazerImporter.Import(oszPath);
        }

        private static string patch(string[] lines, Edits e, IReadOnlyList<string> hitObjectLines)
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

                    // Replace the entire hit-object body with the current (edited) lines.
                    if (section == "HitObjects")
                    {
                        foreach (string hitObject in hitObjectLines)
                            sb.Append(hitObject).Append('\n');
                    }

                    continue;
                }

                // Skip the original hit-object lines; they were re-emitted under the header above.
                if (section == "HitObjects")
                    continue;

                sb.Append(section switch
                {
                    "General" => replaceGeneral(raw, e),
                    "Metadata" => replaceMetadata(raw, e),
                    "Difficulty" => replaceDifficulty(raw, e),
                    _ => raw,
                });
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static string replaceGeneral(string line, Edits e)
        {
            return key(line) switch
            {
                "StackLeniency" => $"StackLeniency: {e.StackLeniency.ToString("0.###", CultureInfo.InvariantCulture)}",
                _ => line,
            };
        }

        private static string replaceMetadata(string line, Edits e)
        {
            return key(line) switch
            {
                "Title" => $"Title:{e.Title}",
                "TitleUnicode" => $"TitleUnicode:{e.TitleUnicode}",
                "Artist" => $"Artist:{e.Artist}",
                "ArtistUnicode" => $"ArtistUnicode:{e.ArtistUnicode}",
                "Creator" => $"Creator:{e.Creator}",
                "Version" => $"Version:{e.Version}",
                "Source" => $"Source:{e.Source}",
                "Tags" => $"Tags:{e.Tags}",
                _ => line,
            };
        }

        private static string replaceDifficulty(string line, Edits e)
        {
            return key(line) switch
            {
                "HPDrainRate" => $"HPDrainRate:{num(e.Hp)}",
                "CircleSize" => $"CircleSize:{num(e.Cs)}",
                "OverallDifficulty" => $"OverallDifficulty:{num(e.Od)}",
                "ApproachRate" => $"ApproachRate:{num(e.Ar)}",
                _ => line,
            };
        }

        private static string key(string line)
        {
            int sep = line.IndexOf(':');
            return sep < 0 ? string.Empty : line[..sep].Trim();
        }

        private static string num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string sanitise(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "beatmap.osz" : cleaned;
        }
    }
}
