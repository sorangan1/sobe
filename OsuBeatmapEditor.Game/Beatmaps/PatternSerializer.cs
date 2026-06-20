using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Serializes a saved "pattern" (a captured selection of hit objects) to/from a portable string, for the
    /// Pattern Gallery. A pattern is stored as a <b>time-normalized .osu hit-object fragment</b>: the selection's
    /// raw .osu lines with their times shifted so the earliest object starts at 0 (positions kept absolute).
    /// Round-tripping reuses the real <see cref="OsuFileDecoder"/> - we wrap the lines in a minimal in-memory
    /// .osu and decode it - so reconstructed objects carry full geometry (Path, ControlPoints, NodeSamples).
    ///
    /// To keep a slider's rhythmic length (e.g. "half a beat") when pasted into a map with a different tempo /
    /// slider multiplier, each slider also stores its <b>source velocity</b> (SliderMultiplier · effective SV at
    /// capture). On paste the editor reproduces that velocity with green lines, so the slider occupies the same
    /// number of beats and keeps its shape. (v1 patterns have no velocities and just re-time to the target map.)
    /// </summary>
    public static class PatternSerializer
    {
        private const int format_version = 2;

        private static readonly JsonSerializerOptions json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>The on-the-wire payload: a version tag, the normalized .osu lines, and per-line source velocities.</summary>
        private sealed class Payload
        {
            public int Version { get; set; } = format_version;
            public List<string> Objects { get; set; } = new List<string>();

            /// <summary>Per-object source velocity (SliderMultiplier·SV) for sliders; null for non-sliders / v1.</summary>
            public List<double?>? Velocities { get; set; }

            /// <summary>The source map's beat length (ms) at the pattern start, so paste can preserve rhythmic spacing.</summary>
            public double? BeatLength { get; set; }
        }

        /// <summary>A reconstructed pattern: its objects, each slider's source velocity, and the source beat length.</summary>
        public readonly record struct DeserializedPattern(
            IReadOnlyList<HitObjectModel> Objects,
            IReadOnlyList<double?> SourceVelocities,
            double? SourceBeatLength);

        /// <summary>
        /// Serializes the given objects (time-normalized to start at 0). <paramref name="sliderVelocity"/>, when
        /// supplied, returns a slider's source velocity (SliderMultiplier·effective SV) so rhythm can be preserved
        /// on paste; return null for non-sliders. <paramref name="sourceBeatLength"/> is the source map's beat
        /// length so paste can rescale the inter-note spacing to the target tempo (keeping notes on the grid).
        /// </summary>
        public static string Serialize(IEnumerable<HitObjectModel> objects, Func<HitObjectModel, double?>? sliderVelocity = null, double? sourceBeatLength = null)
        {
            var ordered = objects.Where(o => !string.IsNullOrEmpty(o.RawLine)).OrderBy(o => o.StartTime).ToList();
            if (ordered.Count == 0)
                return JsonSerializer.Serialize(new Payload(), json);

            double baseTime = ordered.Min(o => o.StartTime);
            var payload = new Payload
            {
                Objects = ordered.Select(o => HitObjectLineEditor.ShiftTime(o.RawLine, -baseTime)).ToList(),
                Velocities = sliderVelocity == null ? null : ordered.Select(sliderVelocity).ToList(),
                BeatLength = sourceBeatLength,
            };
            return JsonSerializer.Serialize(payload, json);
        }

        /// <summary>Rebuilds the pattern's hit objects (full models, head at time 0) from stored content.</summary>
        public static IReadOnlyList<HitObjectModel> Deserialize(string content) => DeserializeFull(content).Objects;

        /// <summary>Rebuilds the pattern's objects plus each slider's source velocity (aligned by index).</summary>
        public static DeserializedPattern DeserializeFull(string content)
        {
            var empty = new DeserializedPattern(Array.Empty<HitObjectModel>(), Array.Empty<double?>(), null);
            if (string.IsNullOrWhiteSpace(content))
                return empty;

            Payload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<Payload>(content, json);
            }
            catch
            {
                return empty;
            }

            var lines = payload?.Objects ?? new List<string>();
            if (lines.Count == 0)
                return empty;

            // A minimal valid .osu around the stored lines: a default red line + SliderMultiplier so the decoder
            // can compute placeholder slider durations/paths. The real timing is applied on paste.
            var sb = new StringBuilder();
            sb.Append("osu file format v14\n\n");
            sb.Append("[Difficulty]\nSliderMultiplier:1.4\nSliderTickRate:1\n\n");
            sb.Append("[TimingPoints]\n0,500,4,2,0,100,1,0\n\n");
            sb.Append("[HitObjects]\n");
            foreach (var line in lines)
                sb.Append(line).Append('\n');

            using var reader = new StringReader(sb.ToString());
            var objects = OsuFileDecoder.Decode(reader).HitObjects;

            // Align velocities with the decoded objects (same order as the lines); pad/truncate defensively.
            var velocities = new double?[objects.Count];
            var stored = payload?.Velocities;
            if (stored != null)
            {
                for (int i = 0; i < objects.Count && i < stored.Count; i++)
                    velocities[i] = stored[i];
            }

            return new DeserializedPattern(objects, velocities, payload?.BeatLength);
        }

        /// <summary>The number of objects a stored pattern contains (without rebuilding full models).</summary>
        public static int ObjectCount(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;
            try
            {
                return JsonSerializer.Deserialize<Payload>(content, json)?.Objects.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
