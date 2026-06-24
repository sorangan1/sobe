using System;
using System.IO;
using System.IO.Compression;
using osu.Framework.Logging;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Packs a beatmap set back into a standard <c>.osz</c> archive on disk, reading the set's files from
    /// osu!lazer's content-addressable store. The archive can then be imported into osu!(stable), shared, or
    /// backed up. Mirrors osu!lazer's own "Export" (write into a known <c>exports/</c> folder, then reveal it)
    /// since osu!framework has no native save-file dialog.
    /// </summary>
    public static class BeatmapArchiveExporter
    {
        /// <summary>
        /// Writes the set's files into a <c>.osz</c> under <paramref name="exportsDir"/>. On success returns
        /// <c>null</c> and sets <paramref name="outputPath"/> to the written file; otherwise returns an error.
        /// </summary>
        public static string? Export(BeatmapSetModel set, string exportsDir, out string outputPath)
        {
            outputPath = string.Empty;

            if (set.OriginalFiles.Count == 0)
                return "This map has no saved files to export.";
            if (string.IsNullOrEmpty(set.DataDirectory))
                return "osu!lazer's data directory could not be located.";

            try
            {
                Directory.CreateDirectory(exportsDir);

                string name = LazerRealmFiles.ValidFilename($"{set.Artist} - {set.Title} ({set.Author})");
                string target = Path.Combine(exportsDir, $"{name}.osz");
                target = uniquePath(target);

                using (var zipStream = new FileStream(target, FileMode.Create, FileAccess.Write))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    int written = 0;
                    foreach (var (filename, hash) in set.OriginalFiles)
                    {
                        string? source = LazerFileStore.ResolvePath(set.DataDirectory, hash);
                        if (source == null || !File.Exists(source))
                            continue;

                        var entry = archive.CreateEntry(filename, CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        using var fileStream = File.OpenRead(source);
                        fileStream.CopyTo(entryStream);
                        written++;
                    }

                    if (written == 0)
                        return "None of the map's files could be located in osu!lazer's store.";
                }

                outputPath = target;
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapArchiveExporter: export failed");
                return $"Export failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Writes just the given difficulty's <c>.osu</c> (its last saved state, read from osu!lazer's store) into
        /// <paramref name="exportsDir"/>, named <c>Artist - Title (Creator) [Version].osu</c>. On success returns
        /// <c>null</c> and sets <paramref name="outputPath"/>; otherwise returns an error message.
        /// </summary>
        public static string? ExportDifficultyOsu(BeatmapSetModel set, BeatmapDifficultyModel difficulty, string exportsDir, out string outputPath)
        {
            outputPath = string.Empty;

            if (string.IsNullOrEmpty(difficulty.OsuFileHash))
                return "This difficulty hasn't been saved yet, so it has no .osu to export.";
            if (string.IsNullOrEmpty(set.DataDirectory))
                return "osu!lazer's data directory could not be located.";

            string? source = LazerFileStore.ResolvePath(set.DataDirectory, difficulty.OsuFileHash);
            if (source == null || !File.Exists(source))
                return "The difficulty's .osu could not be located in osu!lazer's store.";

            try
            {
                Directory.CreateDirectory(exportsDir);

                string name = LazerRealmFiles.ValidFilename($"{set.Artist} - {set.Title} ({set.Author}) [{difficulty.DifficultyName}]");
                string target = uniquePath(Path.Combine(exportsDir, $"{name}.osu"));
                File.Copy(source, target);

                outputPath = target;
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BeatmapArchiveExporter: .osu export failed");
                return $"Export failed: {ex.Message}";
            }
        }

        /// <summary>Appends " (n)" before the extension until the path is free, so repeated exports don't clobber.</summary>
        private static string uniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 2; ; i++)
            {
                string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                if (!File.Exists(candidate))
                    return candidate;
            }
        }
    }
}
