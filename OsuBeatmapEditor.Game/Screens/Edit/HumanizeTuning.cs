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
        // ─── FLOW (stream sweep vs settle-on-each-note) ────────────────────────────────────────────────────
        // Each object gets a flow factor φ from its surrounding note-to-note time gaps: short, fast gaps on both
        // sides → φ≈1 (the cursor sweeps THROUGH it like a stream); a long gap on either side → φ≈0 (it eases in
        // and settles on that note, like an isolated hit). φ scales the spline's velocity at the object, and the
        // SAME φ·velocity is shared by the gap before and after, so the path is always C1 (it can never teleport).
        public static float FlowFastMs = 110f;   // gaps THIS short on both sides = full flow-through (a stream)
        public static float FlowSlowMs = 220f;   // a gap THIS long either side = settle on the note (a jump)
        // Stream "path of least effort": a small note-to-note zig-zag in a stream isn't traced — the cursor goes
        // nearly straight through it, following it only once its amplitude passes a threshold. A binomial smooth
        // (¼,½,¼) removes just the ALTERNATING component (so smooth curved streams are kept, only zig-zags flatten);
        // a soft-threshold on the removed part straightens up to the threshold and follows the excess. Streams only.
        public static float StreamStraightenAmount = 0.29f;    // 0 = trace every wiggle, 1 = fully cut small zig-zags (user-tuned)
        public static float StreamStraightenThreshold = 1.6f;  // zig-zag half-amplitude (× radius) below which it's straightened; above, followed (user-tuned)

        // ─── ARC (lateral bow on jumps — the main "human vs robot" trait) ───────────────────────────────────
        // Replay analysis: real cursors bow OFF the straight line between two objects by 6-21% of the jump (more on
        // shorter jumps). arcFrac = ArcAmount × ArcFalloffNorm / (ArcFalloffNorm + dNorm) fits the measured curve.
        // Only applied where flow is low (jumps); the Hermite spline already curves streams from their note layout.
        public static float ArcAmount = 0.35f;       // bow as a fraction of the jump as dNorm→0 (then decays with distance)
        public static float ArcFalloffNorm = 90f;    // normalised distance at which the bow fraction halves
        // Handedness: arcs bow away from a fixed wrist pivot (offset from the playfield centre), so the side is
        // consistent and never flips. Replay-calibrated to a pivot below + slightly off-centre. osu! y is DOWN,
        // so a positive Y is BELOW the play area; flip HandPivotX's sign to swap the handedness (right vs left).
        public static float HandPivotX = -40f;       // pivot offset from centre on X (+ = right)
        public static float HandPivotY = 350f;       // pivot offset from centre on Y (+ = below the play area)

        // ─── AIM / TREMOR (the only high-frequency "noise") ────────────────────────────────────────────────
        public static float AimErrorAmount = 0.14f;  // lands off-centre: × radius × uniform(0..1) (user-tuned tighter than the ~0.34 r replay median)
        public static float TremorAmount = 1f;        // multiplier on the faint ~0.13px hand wobble (replay shake is tiny)

        // ─── OVERSHOOT (a medium jump flicks just past, then settles — replay peak ~100-160 normalised) ──────
        public static float OvershootGain = 0.046f;    // peak overshoot as a fraction of the jump (0 = none) (user-tuned)
        public static float OvershootPeakNorm = 130f;  // jump size (normalised) where overshoot peaks; fades above & below

        // ─── SLIDERS (lazy follow — cut curvature smoothly, never stall) ────────────────────────────────────
        // Real cursors don't trace the path exactly: on a curve they "omit" it, cutting the corner into an arc for
        // comfort. Modelled as a CENTRED moving average of the ball path (smooth, always tracking the ball — no
        // stop-and-wait or lag). The window decides how much curvature is cut; straights stay on the ball.
        public static float SliderCutWindow = 0.4f;      // averaging half-window as a fraction of the slider duration (bigger = more corner cut) (user-tuned)
        public static float SliderLaziness = 0.76f;       // overall blend: 0 = trace the path exactly, 1 = full cut (user-tuned)
        public static float SliderEdgeFadeMs = 72f;       // keep the head/tail on-path over this many ms (those are actually hit) (user-tuned)
        // A "kick slider" (1/4, 1/8, ...) is so short it's played like a circle — the player just taps the head and
        // moves on instead of tracing it. Sliders at or under this duration release at the head (stream like circles).
        public static float KickSliderMaxMs = 80f;
        // Momentum: how much a fast-flowing rhythm makes the player release a slider early (skip the rest and flow on).
        // 0 = always follow to the end; 1 = a full-flow burst releases right at the head. In between = a brief "amago".
        public static float SliderFlowRelease = 0.8f;
        // Reverse (repeat) sliders: the player often parks near the MIDDLE rather than chasing the ball back and forth
        // (the ball keeps returning within reach). 0 = follow the ball normally, 1 = sit on the path midpoint.
        public static float ReverseLaziness = 0.6f;
        public static float SliderReleaseFrac = 0.012f;  // let go this fraction of the slider early (replay median ~1%)..
        public static float SliderReleaseMaxMs = 8f;     // ..capped at this many ms

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
