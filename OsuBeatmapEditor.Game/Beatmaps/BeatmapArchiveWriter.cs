using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Writes a new beatmap to a standard <c>.osz</c> archive (a zip containing the <c>.osu</c>
    /// text file plus the audio track). The archive is consumed by osu!lazer's own importer,
    /// so we never write to lazer's database directly.
    /// </summary>
    public static class BeatmapArchiveWriter
    {
        /// <summary>
        /// Builds the .osz for the given request and returns its absolute path.
        /// </summary>
        public static string Write(NewBeatmapRequest request)
        {
            if (!request.IsValid)
                throw new InvalidOperationException("New beatmap request is incomplete.");

            string audioFilename = ResolveAudioFilename(request);
            string osuFilename = OsuFilename(request);

            string exportDir = Path.Combine(Path.GetTempPath(), "osu-editor-exports");
            Directory.CreateDirectory(exportDir);

            string oszPath = Path.Combine(exportDir,
                sanitiseFilename($"{request.Artist} - {request.Title}.osz"));

            // Overwrite any previous export with the same name.
            if (File.Exists(oszPath))
                File.Delete(oszPath);

            using (var archive = ZipFile.Open(oszPath, ZipArchiveMode.Create))
            {
                var osuEntry = archive.CreateEntry(osuFilename, CompressionLevel.NoCompression);
                using (var writer = new StreamWriter(osuEntry.Open(), new UTF8Encoding(false)))
                    writer.Write(BuildOsuText(request, audioFilename, uninheritedTimingOnly: false));

                archive.CreateEntryFromFile(request.AudioPath, audioFilename, CompressionLevel.NoCompression);
            }

            return oszPath;
        }

        /// <summary>The stored audio filename for the request (reused source name, or derived from the path's extension).</summary>
        public static string ResolveAudioFilename(NewBeatmapRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.AudioFileName))
                // Reuse the source set's audio filename (its on-disk path is content-addressed, no extension).
                return sanitiseFilename(request.AudioFileName);

            string audioExtension = Path.GetExtension(request.AudioPath).ToLowerInvariant();
            if (string.IsNullOrEmpty(audioExtension))
                audioExtension = ".mp3";
            return $"audio{audioExtension}";
        }

        /// <summary>The .osu filename for the request, "{Artist} - {Title} ({Creator}) [{Diff}].osu".</summary>
        public static string OsuFilename(NewBeatmapRequest request) =>
            sanitiseFilename($"{request.Artist} - {request.Title} ({request.Creator}) [{request.DifficultyName}].osu");

        /// <summary>
        /// Builds the .osu text for a new beatmap. When <paramref name="uninheritedTimingOnly"/> is true, only
        /// uninherited (red/BPM) timing points are kept - inherited (green/SV/kiai) points are dropped. No
        /// bookmarks are ever written. Shared by the .osz exporter and the direct realm creator.
        /// </summary>
        public static string BuildOsuText(NewBeatmapRequest request, string audioFilename, bool uninheritedTimingOnly)
        {
            // Beat length in milliseconds for the uninherited (red) timing point.
            double beatLength = 60000.0 / request.Bpm;
            string bl = beatLength.ToString("0.######", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.Append("osu file format v14\n\n");

            sb.Append("[General]\n");
            sb.Append($"AudioFilename: {audioFilename}\n");
            sb.Append("AudioLeadIn: 0\n");
            sb.Append("PreviewTime: -1\n");
            sb.Append("Countdown: 0\n");
            sb.Append("SampleSet: Normal\n");
            sb.Append("StackLeniency: 0.7\n");
            sb.Append("Mode: 0\n");
            sb.Append("LetterboxInBreaks: 0\n");
            sb.Append("WidescreenStoryboard: 0\n\n");

            sb.Append("[Editor]\n");
            sb.Append("DistanceSpacing: 1\n");
            sb.Append("BeatDivisor: 4\n");
            sb.Append("GridSize: 4\n");
            sb.Append("TimelineZoom: 1\n\n");

            sb.Append("[Metadata]\n");
            sb.Append($"Title:{request.Title}\n");
            sb.Append($"TitleUnicode:{request.Title}\n");
            sb.Append($"Artist:{request.Artist}\n");
            sb.Append($"ArtistUnicode:{request.Artist}\n");
            sb.Append($"Creator:{request.Creator}\n");
            sb.Append($"Version:{request.DifficultyName}\n");
            sb.Append("Source:\n");
            sb.Append("Tags:\n");
            sb.Append("BeatmapID:0\n");
            sb.Append("BeatmapSetID:-1\n\n");

            sb.Append("[Difficulty]\n");
            sb.Append("HPDrainRate:5\n");
            sb.Append("CircleSize:4\n");
            sb.Append("OverallDifficulty:5\n");
            sb.Append("ApproachRate:5\n");
            sb.Append("SliderMultiplier:1.4\n");
            sb.Append("SliderTickRate:1\n\n");

            sb.Append("[Events]\n");
            sb.Append("//Background and Video events\n");
            sb.Append("//Break Periods\n");
            sb.Append("//Storyboard Layer 0 (Background)\n");
            sb.Append("//Storyboard Layer 1 (Fail)\n");
            sb.Append("//Storyboard Layer 2 (Pass)\n");
            sb.Append("//Storyboard Layer 3 (Foreground)\n");
            sb.Append("//Storyboard Layer 4 (Overlay)\n");
            sb.Append("//Storyboard Sound Samples\n\n");

            sb.Append("[TimingPoints]\n");
            var timingLines = request.TimingPointLines;
            if (uninheritedTimingOnly && timingLines is { Count: > 0 })
                timingLines = timingLines.Where(isUninherited).ToList();

            if (timingLines is { Count: > 0 })
            {
                // Carry over the source set's timing (verbatim, or BPM-only when filtered).
                foreach (string line in timingLines)
                    sb.Append(line).Append('\n');
                sb.Append('\n');
            }
            else
            {
                // time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects
                sb.Append($"0,{bl},4,1,0,100,1,0\n\n");
            }

            sb.Append("[HitObjects]\n");

            return sb.ToString();
        }

        /// <summary>A timing line is uninherited (red/BPM) when field 6 is "1"; legacy lines without it are uninherited.</summary>
        private static bool isUninherited(string line)
        {
            string[] parts = line.Split(',');
            return parts.Length <= 6 || parts[6].Trim() == "1";
        }

        private static string sanitiseFilename(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrEmpty(cleaned) ? "beatmap" : cleaned;
        }
    }
}
