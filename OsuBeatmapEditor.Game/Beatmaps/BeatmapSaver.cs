using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using osu.Framework.Graphics;

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
            public float SliderMultiplier = 1.4f, SliderTickRate = 1f;

            /// <summary>The computed star rating to persist to the realm (so the carousel/lazer reflect it). -1 = leave as-is.</summary>
            public double StarRating = -1;

            /// <summary>The map's own combo colours ([Colours] ComboN), in order. Empty = no [Colours] section.</summary>
            public List<Colour4> ComboColours = new List<Colour4>();
        }

        /// <summary>
        /// Produces the patched .osu text for an edited difficulty (re-emitting [HitObjects],
        /// [TimingPoints] and [Editor] Bookmarks, and replacing [General]/[Metadata]/[Difficulty] lines
        /// while preserving everything else verbatim), or <c>null</c> if the original .osu can't be resolved.
        /// Shared by the .osz exporter (<see cref="Save"/>) and the in-place realm writer
        /// (<see cref="BeatmapRealmWriter"/>).
        /// </summary>
        public static string? BuildPatchedOsu(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, Edits edits)
        {
            string? osuPath = LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);
            if (osuPath == null)
                return null;

            // Re-emit the [HitObjects] section from the (possibly edited) object list so deletions/edits
            // persist, while every other line is preserved verbatim.
            var hitObjectLines = parsed.HitObjects
                .Select(o => o.RawLine)
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            // Re-emit the timing points (red/green stable lines) so add/delete/edit persist; the lazer
            // importer translates them into its own per-property control points on import.
            var timingPointLines = parsed.TimingPointModels
                .OrderBy(tp => tp.Time)
                .Select(TimingPointLineEditor.Encode)
                .ToList();

            // The [Editor] Bookmarks line, re-emitted from the (possibly edited) bookmark list, or null when
            // there are none (then the line is dropped on save).
            string? bookmarksLine = parsed.Bookmarks.Count > 0
                ? "Bookmarks: " + string.Join(",", parsed.Bookmarks)
                : null;

            // The map's combo colours, re-emitted as [Colours] ComboN lines (empty list => no section).
            var colourLines = edits.ComboColours
                .Select((c, i) => $"Combo{i + 1} : {toByte(c.R)},{toByte(c.G)},{toByte(c.B)}")
                .ToList();

            return patch(File.ReadAllLines(osuPath), edits, hitObjectLines, timingPointLines, bookmarksLine, colourLines);
        }

        private static int toByte(float component) => (int)Math.Round(Math.Clamp(component, 0f, 1f) * 255f);

        public static bool Save(BeatmapSetModel set, BeatmapDifficultyModel difficulty, ParsedBeatmap parsed, Edits edits)
        {
            string? patched = BuildPatchedOsu(set, difficulty, parsed, edits);
            if (patched == null)
                return false;

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

        private static string patch(string[] lines, Edits e, IReadOnlyList<string> hitObjectLines, IReadOnlyList<string> timingPointLines, string? bookmarksLine, IReadOnlyList<string> colourLines)
        {
            var sb = new StringBuilder();
            string section = string.Empty;
            bool editorSectionSeen = false;
            bool coloursSectionSeen = false;

            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    if (section == "Editor")
                        editorSectionSeen = true;
                    if (section == "Colours")
                        coloursSectionSeen = true;
                    sb.Append(raw).Append('\n');

                    // Replace the entire hit-object body with the current (edited) lines.
                    if (section == "HitObjects")
                    {
                        foreach (string hitObject in hitObjectLines)
                            sb.Append(hitObject).Append('\n');
                    }

                    // Replace the entire timing-point body with the current (edited) lines.
                    if (section == "TimingPoints")
                    {
                        foreach (string timingPoint in timingPointLines)
                            sb.Append(timingPoint).Append('\n');
                    }

                    // Re-emit the (possibly edited) bookmarks at the top of the [Editor] section.
                    if (section == "Editor" && bookmarksLine != null)
                        sb.Append(bookmarksLine).Append('\n');

                    // Re-emit the (possibly edited) map combo colours at the top of the [Colours] section.
                    if (section == "Colours")
                    {
                        foreach (string colour in colourLines)
                            sb.Append(colour).Append('\n');
                    }

                    continue;
                }

                // Skip the original hit-object / timing-point lines; they were re-emitted under the header.
                if (section == "HitObjects" || section == "TimingPoints")
                    continue;

                // Skip the original Bookmarks line; the current one was re-emitted under the [Editor] header.
                if (section == "Editor" && trimmed.StartsWith("Bookmarks", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip the original ComboN lines; the current ones were re-emitted under the [Colours] header
                // (other [Colours] keys like SliderTrackOverride / SliderBorder are kept verbatim).
                if (section == "Colours" && trimmed.StartsWith("Combo", StringComparison.OrdinalIgnoreCase))
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

            // If the file had no [Editor] section but we have bookmarks, add one so they persist.
            if (!editorSectionSeen && bookmarksLine != null)
                sb.Append("\n[Editor]\n").Append(bookmarksLine).Append('\n');

            // Likewise add a [Colours] section if the file had none but the map now has combo colours.
            if (!coloursSectionSeen && colourLines.Count > 0)
            {
                sb.Append("\n[Colours]\n");
                foreach (string colour in colourLines)
                    sb.Append(colour).Append('\n');
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
                "SliderMultiplier" => $"SliderMultiplier:{num(e.SliderMultiplier)}",
                "SliderTickRate" => $"SliderTickRate:{num(e.SliderTickRate)}",
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
