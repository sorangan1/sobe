using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>The kinds of osu! Standard hit objects we currently distinguish.</summary>
    public enum HitObjectKind
    {
        Circle,
        Slider,
        Spinner,
    }

    /// <summary>The spline family a slider path segment is interpreted with (mirrors osu!lazer's SplineType).</summary>
    public enum SliderSplineType
    {
        Catmull,
        BSpline,
        Linear,
        PerfectCurve,
    }

    /// <summary>
    /// The type of a slider path segment (mirrors osu!lazer's <c>PathType</c>): a spline family plus an
    /// optional B-spline degree (null = full-order Bézier, as written by the plain <c>B</c> curve letter).
    /// </summary>
    public readonly record struct SliderPathType(SliderSplineType Type, int? Degree = null)
    {
        public static readonly SliderPathType Bezier = new SliderPathType(SliderSplineType.BSpline);
        public static readonly SliderPathType Linear = new SliderPathType(SliderSplineType.Linear);
        public static readonly SliderPathType PerfectCurve = new SliderPathType(SliderSplineType.PerfectCurve);
        public static readonly SliderPathType Catmull = new SliderPathType(SliderSplineType.Catmull);
    }

    /// <summary>
    /// One slider control point in absolute osu!pixels, head first (mirrors osu!lazer's <c>PathControlPoint</c>).
    /// A non-null <see cref="Type"/> means this point STARTS a new path segment of that type; null means it
    /// continues the previous segment. The first control point always has a type. In the editor a typed point
    /// renders as a "red anchor" (segment boundary / sharp corner); a typeless one renders white.
    /// </summary>
    public readonly record struct SliderControlPoint(float X, float Y, SliderPathType? Type = null)
    {
        public Vector2 Position => new Vector2(X, Y);

        public bool IsSegmentStart => Type != null;

        public SliderControlPoint(Vector2 p, SliderPathType? type = null) : this(p.X, p.Y, type) { }
    }

    /// <summary>
    /// A hitsound sample bank (the .osu sample set). <see cref="Auto"/> = 0 = inherit the active timing point's
    /// sample set (resolved at playback, like volume); 1 = Normal, 2 = Soft, 3 = Drum.
    /// </summary>
    public enum SampleBank
    {
        Auto,
        Normal,
        Soft,
        Drum,
    }

    /// <summary>
    /// The hitsounds for a single slider node (head / a repeat / the tail): the addition bitfield
    /// (whistle/finish/clap) plus its resolved normal and addition banks. One per node, count = slides + 1.
    /// </summary>
    public readonly record struct NodeSample(int HitSound, SampleBank NormalBank, SampleBank AdditionBank);

    /// <summary>
    /// A single hit object reduced to what the playfield needs to draw it.
    /// <see cref="Path"/> holds the computed slider polyline (in osu!pixel coordinates, head first);
    /// it is null for circles and spinners. <see cref="Duration"/> is the full slider travel time
    /// (across all repeats) in milliseconds; <see cref="Slides"/> is the number of spans (1 = no repeat).
    /// </summary>
    public readonly record struct HitObjectModel(
        float X,
        float Y,
        double StartTime,
        HitObjectKind Kind,
        IReadOnlyList<Vector2>? Path,
        double Duration = 0,
        int Slides = 1,
        int ComboNumber = 1,
        int ComboIndex = 0,
        int HitSound = 0,
        SampleBank NormalBank = SampleBank.Normal,
        SampleBank AdditionBank = SampleBank.Normal,
        float SampleVolume = 1f,
        string RawLine = "",
        int Id = -1,
        int StackHeight = 0,
        IReadOnlyList<SliderControlPoint>? ControlPoints = null,
        IReadOnlyList<NodeSample>? NodeSamples = null);

    /// <summary>
    /// A timing point reduced to what the timeline needs: its time, whether it is uninherited (BPM), and its
    /// display value - the BPM for uninherited points, or the slider-velocity multiplier for inherited ones.
    /// </summary>
    public readonly record struct TimingMarker(int Time, bool Uninherited, double Value = 0);

    /// <summary>An uninherited (BPM) timing point with its beat length (ms) and meter, for the beat grid.</summary>
    public readonly record struct BeatPoint(double Time, double BeatLength, int Meter);

    /// <summary>The effective slider-velocity multiplier in force from <see cref="Time"/> onward (resets to 1 at each red line).</summary>
    public readonly record struct VelocityPoint(double Time, double Multiplier);

    /// <summary>A kiai-time span in milliseconds.</summary>
    public readonly record struct KiaiSection(int Start, int End);

    /// <summary>A break period (no objects) in milliseconds, from the <c>[Events]</c> section.</summary>
    public readonly record struct BreakPeriod(int Start, int End);

    /// <summary>
    /// A fully-editable timing point, kept in the osu! stable model (red = uninherited/BPM, green =
    /// inherited/SV) with every field needed to re-emit its <c>[TimingPoints]</c> line losslessly. On save
    /// these stable lines are written verbatim and osu!lazer's importer translates them into its own
    /// per-property control points - so the editor stays entirely in the stable model.
    /// </summary>
    public readonly record struct TimingPointModel(
        int Id,
        double Time,
        double BeatLength,
        int Meter,
        int SampleSet,
        int SampleIndex,
        int Volume,
        bool Uninherited,
        int Effects,
        string RawLine = "")
    {
        /// <summary>effects bit 0 = kiai.</summary>
        public bool Kiai => (Effects & 1) != 0;

        /// <summary>BPM for an uninherited line; 0 for inherited.</summary>
        public double Bpm => Uninherited && BeatLength > 0 ? 60000.0 / BeatLength : 0;

        /// <summary>The slider-velocity multiplier an inherited line encodes (-100/beatLength).</summary>
        public double SliderVelocity => Uninherited ? 1 : Math.Clamp(-100 / BeatLength, 0.1, 10);
    }

    /// <summary>
    /// The decoded contents of a .osu file, limited to what the editor playfield renders today.
    /// </summary>
    public class ParsedBeatmap
    {
        /// <summary>osu! Standard playfield width in osu!pixels.</summary>
        public const float PLAYFIELD_WIDTH = 512;

        /// <summary>osu! Standard playfield height in osu!pixels.</summary>
        public const float PLAYFIELD_HEIGHT = 384;

        // Difficulty settings.
        public float HpDrainRate { get; set; } = 5;
        public float CircleSize { get; set; } = 5;
        public float OverallDifficulty { get; set; } = 5;
        public float ApproachRate { get; set; } = -1; // -1 => defaults to OverallDifficulty (old maps)

        /// <summary>How aggressively nearby objects stack (osu! [General] StackLeniency; default 0.7).</summary>
        public float StackLeniency { get; set; } = 0.7f;

        /// <summary>Base slider velocity multiplier ([Difficulty] SliderMultiplier; default 1.4), used to time placed sliders.</summary>
        public float SliderMultiplier { get; set; } = 1.4f;

        /// <summary>Slider tick rate ([Difficulty] SliderTickRate; default 1), the number of ticks per beat.</summary>
        public float SliderTickRate { get; set; } = 1f;

        /// <summary>The ".osu file format vN" version (default 14). 128+ is the lazer format, which relaxes some slider rules.</summary>
        public int FormatVersion { get; set; } = 14;

        // Metadata.
        public string Title { get; set; } = string.Empty;
        public string TitleUnicode { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string ArtistUnicode { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;

        public string AudioFilename { get; set; } = string.Empty;

        public string BackgroundFilename { get; set; } = string.Empty;

        /// <summary>Mapper-chosen preview start time in ms, or -1 if unset.</summary>
        public int PreviewTime { get; set; } = -1;

        public List<HitObjectModel> HitObjects { get; } = new List<HitObjectModel>();

        /// <summary>The editable, lossless timing points in the osu! stable model (red BPM + green SV lines).</summary>
        public List<TimingPointModel> TimingPointModels { get; } = new List<TimingPointModel>();

        /// <summary>Timing points (for the timeline markers): red = uninherited (BPM), green = inherited (SV).</summary>
        public List<TimingMarker> TimingPoints { get; } = new List<TimingMarker>();

        /// <summary>Uninherited timing points with beat length + meter, used to build the top timeline's beat grid.</summary>
        public List<BeatPoint> BeatPoints { get; } = new List<BeatPoint>();

        /// <summary>Effective slider-velocity multiplier over time (time-ordered), for timing placed sliders.</summary>
        public List<VelocityPoint> VelocityPoints { get; } = new List<VelocityPoint>();

        /// <summary>Editor bookmark times (ms).</summary>
        public List<int> Bookmarks { get; } = new List<int>();

        /// <summary>The map's own combo colours from the <c>[Colours]</c> section (ComboN), in order. Empty if none.</summary>
        public List<osu.Framework.Graphics.Colour4> ComboColours { get; } = new List<osu.Framework.Graphics.Colour4>();

        /// <summary>Kiai-time spans (ms). <see cref="KiaiSection.End"/> is <see cref="int.MaxValue"/> if open-ended.</summary>
        public List<KiaiSection> KiaiSections { get; } = new List<KiaiSection>();

        /// <summary>Break periods (ms) parsed from the <c>[Events]</c> section (event type 2).</summary>
        public List<BreakPeriod> Breaks { get; } = new List<BreakPeriod>();

        /// <summary>
        /// Rebuilds every timing-derived list (<see cref="TimingPoints"/>, <see cref="BeatPoints"/>,
        /// <see cref="VelocityPoints"/>, <see cref="KiaiSections"/>) from <see cref="TimingPointModels"/>.
        /// Call after any timing-point edit so the timeline grid, slider SV and kiai spans stay in sync.
        /// </summary>
        public void RebuildTimingDerived()
        {
            // Order by time, and on a tie put the red (uninherited/BPM) line BEFORE the green (inherited/SV) one.
            // This is load-bearing, not cosmetic: the loop below resets sv=1 when it hits a red line, so if a
            // coincident green sorted first its velocity would be immediately overwritten by the red's reset and
            // the SV would never apply. (List.Sort is unstable, so a plain time-only compare could swap them.)
            TimingPointModels.Sort((a, b) =>
                a.Time != b.Time ? a.Time.CompareTo(b.Time) : b.Uninherited.CompareTo(a.Uninherited));

            TimingPoints.Clear();
            BeatPoints.Clear();
            VelocityPoints.Clear();
            KiaiSections.Clear();

            double sv = 1;
            bool kiaiActive = false;
            int kiaiStart = 0;

            foreach (var tp in TimingPointModels)
            {
                double markerValue = tp.Uninherited ? tp.Bpm : tp.SliderVelocity;
                TimingPoints.Add(new TimingMarker((int)tp.Time, tp.Uninherited, markerValue));

                if (tp.Uninherited)
                {
                    BeatPoints.Add(new BeatPoint(tp.Time, tp.BeatLength, tp.Meter > 0 ? tp.Meter : 4));
                    sv = 1;
                }
                else
                {
                    sv = tp.SliderVelocity;
                }

                VelocityPoints.Add(new VelocityPoint(tp.Time, sv));

                if (tp.Kiai && !kiaiActive)
                {
                    kiaiActive = true;
                    kiaiStart = (int)tp.Time;
                }
                else if (!tp.Kiai && kiaiActive)
                {
                    kiaiActive = false;
                    KiaiSections.Add(new KiaiSection(kiaiStart, (int)tp.Time));
                }
            }

            if (kiaiActive)
                KiaiSections.Add(new KiaiSection(kiaiStart, int.MaxValue));
        }

        /// <summary>Circle radius in osu!pixels derived from CircleSize (the osu! Standard formula).</summary>
        public float CircleRadius => 54.4f - 4.48f * CircleSize;

        /// <summary>Effective approach rate (falls back to OD for legacy maps that omit it).</summary>
        public float EffectiveApproachRate => ApproachRate < 0 ? OverallDifficulty : ApproachRate;

        /// <summary>Fade-in / approach time (ms) for the current AR (the osu! Standard formula).</summary>
        public double Preempt => PreemptFor(EffectiveApproachRate);

        /// <summary>osu! Standard preempt formula: 1800ms at AR0, 1200ms at AR5, 450ms at AR10.</summary>
        public static double PreemptFor(float ar) => ar < 5
            ? 1200 + 600 * (5 - ar) / 5
            : 1200 - 750 * (ar - 5) / 5;
    }
}
