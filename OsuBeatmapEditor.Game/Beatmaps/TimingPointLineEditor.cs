using System.Globalization;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Emits a <c>[TimingPoints]</c> line in the osu! stable format
    /// (<c>time,beatLength,meter,sampleSet,sampleIndex,volume,uninherited,effects</c>) from a
    /// <see cref="TimingPointModel"/>. The stable model covers the full standard line, so we re-emit it
    /// in full; osu!lazer's importer then translates these red/green lines into its own control points.
    /// </summary>
    public static class TimingPointLineEditor
    {
        public static string Encode(TimingPointModel tp)
        {
            string time = tp.Time.ToString("0.###", CultureInfo.InvariantCulture);
            string beatLength = tp.BeatLength.ToString("0.###########", CultureInfo.InvariantCulture);
            return string.Join(',',
                time,
                beatLength,
                tp.Meter.ToString(CultureInfo.InvariantCulture),
                tp.SampleSet.ToString(CultureInfo.InvariantCulture),
                tp.SampleIndex.ToString(CultureInfo.InvariantCulture),
                tp.Volume.ToString(CultureInfo.InvariantCulture),
                tp.Uninherited ? "1" : "0",
                tp.Effects.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Beat length (ms/beat) for a given BPM, for building uninherited lines.</summary>
        public static double BeatLengthFromBpm(double bpm) => bpm > 0 ? 60000.0 / bpm : 500;

        /// <summary>Encoded beat-length field for an inherited line carrying the given SV multiplier.</summary>
        public static double BeatLengthFromSv(double sv) => -100.0 / (sv <= 0 ? 1 : sv);

        /// <summary>Returns <paramref name="effects"/> with the kiai bit (bit 0) set or cleared.</summary>
        public static int WithKiai(int effects, bool kiai) => kiai ? effects | 1 : effects & ~1;
    }
}
