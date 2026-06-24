using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Every tunable number for the "Humanize" Auto cursor, in one place, so the feel can be dialled in by hand.
    /// Edit a value (or use the in-editor live panel) and watch the AU cursor (toggle Humanize in the AU chip's mini-menu).
    ///
    /// Conventions:
    ///  - Distances are osu! NORMALISED units: distance * 50 / circleRadius, so one circle DIAMETER = 100, radius = 50.
    ///    (e.g. 80 ≈ 0.8 of a diameter; 200 ≈ a 2-diameter jump.) This matches osu!'s own aim metric.
    ///  - Times are milliseconds.
    ///  - A "Lo/Hi" pair is a smoothstep ramp. The comment says which side is the "full" (=1) side.
    ///  - Amounts are fractions (of the jump distance, or of the circle radius) unless noted.
    ///
    /// Defaults were calibrated from real osu!lazer replays (the OsuBeatmapEditor.ReplayAnalysis tool).
    /// </summary>
    public static class HumanizeTuning
    {
        // ─── STACK ─────────────────────────────────────────────────────────────────────────────────────────
        // A "stack" = near-identical positions; the cursor stays planted and just taps. Full stack BELOW StackLo,
        // none above StackHi. KEEP WELL UNDER stream spacing (~45-90) or streams jerk note-to-note.
        public static float StackLo = 8f;
        public static float StackHi = 22f;
        // Fraction of the gap the held cursor waits before sliding to the next note (1 = moves only at the very end).
        public static float StackHoldStart = 0.82f;

        // ─── FLOW: stream (smooth B-spline sweep) vs jump (straight line) ───────────────────────────────────
        // Spatial flow: tight spacing flows. Full flow BELOW SpatialFlowLo, none above SpatialFlowHi.
        public static float SpatialFlowLo = 80f;
        public static float SpatialFlowHi = 170f;
        // Rhythm flow: a fast, regular run of circles flows even when spaced far apart ("spaced streams").
        public static float StreamGapFastMs = 110f;   // note-to-note gaps THIS SHORT = full stream cadence
        public static float StreamGapSlowMs = 185f;   // gaps THIS LONG = jump rhythm (no rhythm-flow)
        public static float StreamRegularLo = 0.20f;  // neighbour gaps within this fraction of each other = regular
        public static float StreamRegularHi = 0.60f;  // more different than this = not a regular run

        // ─── OVERSHOOT (flick past a straight jump, then settle) ────────────────────────────────────────────
        public static float OvershootOnLo = 130f;     // starts above this distance..
        public static float OvershootOnHi = 190f;     // ..fully on by here
        public static float OvershootOffLo = 320f;    // then fades out from here..
        public static float OvershootOffHi = 650f;    // ..gone by here (huge jumps land precise)
        public static float OvershootAmount = 0.6f;   // ease-out-back strength; higher = flies further past, then back
        public static float OvershootReversalGate = 0.8f; // back-and-forth suppresses overshoot by this much (loops instead)

        // ─── ARC (perpendicular bow on jumps) ──────────────────────────────────────────────────────────────
        public static float ArcTurnThreshold = 0.2f;  // |turn| above this is a real corner → bow to the OUTSIDE
        public static float ArcOutsideAmount = 0.14f;  // outside-corner bow, fraction of the jump
        public static float ArcFigure8Amount = 0.16f;  // back-and-forth figure-of-eight bow, fraction of the jump
        public static float ArcMaxPx = 260f;           // bow magnitude caps at this jump length (osu!px)

        // ─── AIM ERROR (lands slightly off-centre on circles) ──────────────────────────────────────────────
        public static float AimErrorAmount = 0.6f;     // × radius × uniform(0..1) per note; 0 = always dead-centre

        // ─── JITTER (faint unsteady-hand wobble) ───────────────────────────────────────────────────────────
        public static float JitterAmount = 1f;         // overall multiplier on the ~0.13px wobble; 0 = perfectly smooth
        public static float JitterSteadyDamp = 0.45f;  // how much streams/stacks steady the hand (0 = none, 1 = full)

        // ─── SLIDERS ───────────────────────────────────────────────────────────────────────────────────────
        public static float SliderReleaseFrac = 0.03f; // let go this fraction of the slider early..
        public static float SliderReleaseMaxMs = 12f;   // ..capped at this many ms
        public static float SliderLaziness = 0.7f;      // how much the cursor cuts slider curvature (0 = trace exactly)

        // ─── LONG-PAUSE DRIFT (relax toward the centre between bursts) ──────────────────────────────────────
        public static float CentreDriftSlowLo = 260f;  // gaps longer than this (ms) start drifting to the centre..
        public static float CentreDriftSlowHi = 650f;   // ..fully by here
        public static float CentreDriftAmount = 0.4f;   // how far toward the playfield centre (0 = none, 1 = all the way)

        // ─── defaults snapshot (for the live panel's "Reset") ──────────────────────────────────────────────
        // Captured once, before any tweak, via reflection over the public static float fields above.
        private static readonly Dictionary<string, float> defaults =
            typeof(HumanizeTuning).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(float))
                .ToDictionary(f => f.Name, f => (float)f.GetValue(null)!);

        /// <summary>Restores every value to the calibrated default it had at startup.</summary>
        public static void ResetToDefaults()
        {
            foreach (var f in typeof(HumanizeTuning).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.FieldType == typeof(float) && defaults.TryGetValue(f.Name, out float d))
                    f.SetValue(null, d);
        }
    }
}
