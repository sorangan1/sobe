using System;
using System.Collections.Generic;
using System.Linq;
using osuTK;

namespace OsuBeatmapEditor.Game.Beatmaps.Difficulty
{
    /// <summary>
    /// A faithful, self-contained port of osu!lazer's osu!standard star-rating algorithm (no mods, clock rate 1),
    /// pinned to ppy/osu commit cf7fdc06 (calculator version 20250306 - the last self-contained release before
    /// the score-simulation rewrite). It runs on our <see cref="ParsedBeatmap"/> rather than lazer's object model.
    ///
    /// We need this because the editor only references ppy.osu.Framework, not osu.Game, so there is no built-in
    /// difficulty calculator. Maps created or imported here (which were never opened in osu!lazer) have no stored
    /// star rating; this fills that gap and powers the live SR readout in the editor.
    ///
    /// Numbers match osu!lazer's published SR closely (within floating-point rounding) for that algorithm version;
    /// osu! has rebalanced the algorithm since, so values may differ slightly from the very latest osu! build.
    /// </summary>
    public static class StarRatingCalculator
    {
        private const double difficulty_multiplier = 0.0675;
        private const double performance_base_multiplier = 1.15;

        /// <summary>
        /// Computes the no-mod star rating of <paramref name="beatmap"/>. Returns 0 for empty maps. This is a
        /// pure CPU computation (no allocation of drawables); callers should run it off the update thread.
        /// </summary>
        public static double Calculate(ParsedBeatmap beatmap)
        {
            if (beatmap == null || beatmap.HitObjects.Count == 0)
                return 0;

            var objects = buildDifficultyObjects(beatmap);
            if (objects.Count == 0)
                return 0; // fewer than two hit objects -> no jumps -> 0 stars (matches lazer).

            var aim = new AimSkill(includeSliders: true);
            var speed = new SpeedSkill();

            foreach (var o in objects)
            {
                aim.Process(o);
                speed.Process(o);
            }

            double aimRating = Math.Sqrt(aim.DifficultyValue()) * difficulty_multiplier;
            double speedRating = Math.Sqrt(speed.DifficultyValue()) * difficulty_multiplier;

            double baseAimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double baseSpeedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);

            double basePerformance = Math.Pow(
                Math.Pow(baseAimPerformance, 1.1) +
                Math.Pow(baseSpeedPerformance, 1.1), 1.0 / 1.1);

            return basePerformance > 0.00001
                ? Math.Cbrt(performance_base_multiplier) * 0.027 * (Math.Cbrt(100000 / Math.Pow(2, 1 / 1.1) * basePerformance) + 4)
                : 0;
        }

        // ---------------------------------------------------------------------------------------------------
        // Preprocessing: turn ParsedBeatmap hit objects into difficulty objects (ported OsuDifficultyHitObject).
        // ---------------------------------------------------------------------------------------------------

        private const int normalised_radius = 50;
        private const int normalised_diameter = normalised_radius * 2;
        private const int min_delta_time = 25;
        private const float maximum_slider_radius = normalised_radius * 2.4f;
        private const float assumed_slider_radius = normalised_radius * 1.8f;
        private const double tail_leniency = -36;

        private static List<DiffObject> buildDifficultyObjects(ParsedBeatmap beatmap)
        {
            float radius = beatmap.CircleRadius;
            float diameter = radius * 2;
            double od = beatmap.OverallDifficulty;
            // OsuHitWindows Great half-window = 80 - 6*OD; the difficulty object stores 2x (full window).
            double hitWindowGreat = 2 * (80.0 - 6.0 * od);

            // Pre-compute the per-object geometry (stacked positions, slider lazy cursor path).
            var raw = new List<DiffObject>(beatmap.HitObjects.Count);

            foreach (var ho in beatmap.HitObjects)
            {
                var d = new DiffObject
                {
                    Kind = ho.Kind,
                    StartTime = ho.StartTime,
                    Radius = radius,
                    HitWindowGreat = hitWindowGreat,
                };

                Vector2 stackOffset = stackOffsetFor(ho.StackHeight, diameter);

                if (ho.Kind == HitObjectKind.Spinner)
                {
                    d.StackedPosition = new Vector2(256, 192);
                    d.EndTime = ho.StartTime + ho.Duration;
                }
                else if (ho.Kind == HitObjectKind.Slider && ho.Path != null && ho.Path.Count >= 2)
                {
                    Vector2 head = ho.Path[0];
                    d.StackedPosition = head + stackOffset;
                    d.IsSlider = true;
                    d.Slides = Math.Max(1, ho.Slides);
                    d.Path = ho.Path;
                    d.EndTime = ho.StartTime + ho.Duration;

                    // Tail end position: progress is 1 for an odd number of spans, 0 (back at head) for even.
                    int tailProgress = d.Slides % 2;
                    d.TailStackedPosition = d.StackedPosition + relativePositionAt(ho.Path, tailProgress);

                    computeSliderCursorPosition(d, beatmap, ho, stackOffset);
                }
                else
                {
                    d.StackedPosition = new Vector2(ho.X, ho.Y) + stackOffset;
                    d.EndTime = ho.StartTime;
                }

                raw.Add(d);
            }

            // Build the jump-based difficulty objects (first jump is objects[0] -> objects[1]).
            var diff = new List<DiffObject>();
            for (int i = 1; i < raw.Count; i++)
            {
                var current = raw[i];
                var last = raw[i - 1];

                current.Index = diff.Count;
                current.All = diff;
                current.DeltaTime = current.StartTime - last.StartTime;
                current.StrainTime = Math.Max(current.DeltaTime, min_delta_time);

                setDistances(current, last, current.Index >= 2 ? raw[i - 2] : null, diff);
                diff.Add(current);
            }

            return diff;
        }

        private static Vector2 stackOffsetFor(int stackHeight, float diameter) => new Vector2(stackHeight * diameter * -0.05f);

        /// <summary>Position along the slider path at <paramref name="progress"/> in [0,1], relative to the head.</summary>
        private static Vector2 relativePositionAt(IReadOnlyList<Vector2> path, double progress)
            => SliderGeometry.PointAtFraction(path, progress) - path[0];

        private static void setDistances(DiffObject current, DiffObject last, DiffObject? lastLast, List<DiffObject> diff)
        {
            if (current.IsSlider)
            {
                current.TravelDistance = current.LazyTravelDistance * Math.Pow(1 + (current.Slides - 1) / 2.5, 1.0 / 2.5);
                current.TravelTime = Math.Max(current.LazyTravelTime, min_delta_time);
            }

            if (current.Kind == HitObjectKind.Spinner || last.Kind == HitObjectKind.Spinner)
                return;

            float scalingFactor = normalised_radius / (float)current.Radius;
            if (current.Radius < 30)
            {
                float smallCircleBonus = Math.Min(30 - (float)current.Radius, 5) / 50;
                scalingFactor *= 1 + smallCircleBonus;
            }

            Vector2 lastCursorPosition = getEndCursorPosition(last);

            current.LazyJumpDistance = (current.StackedPosition * scalingFactor - lastCursorPosition * scalingFactor).Length;
            current.MinimumJumpTime = current.StrainTime;
            current.MinimumJumpDistance = current.LazyJumpDistance;

            if (last.IsSlider)
            {
                double lastTravelTime = Math.Max(last.LazyTravelTime, min_delta_time);
                current.MinimumJumpTime = Math.Max(current.StrainTime - lastTravelTime, min_delta_time);

                float tailJumpDistance = (last.TailStackedPosition - current.StackedPosition).Length * scalingFactor;
                current.MinimumJumpDistance = Math.Max(0, Math.Min(
                    current.LazyJumpDistance - (maximum_slider_radius - assumed_slider_radius),
                    tailJumpDistance - maximum_slider_radius));
            }

            if (lastLast != null && lastLast.Kind != HitObjectKind.Spinner)
            {
                Vector2 lastLastCursorPosition = getEndCursorPosition(lastLast);

                Vector2 v1 = lastLastCursorPosition - last.StackedPosition;
                Vector2 v2 = current.StackedPosition - lastCursorPosition;

                float dot = Vector2.Dot(v1, v2);
                float det = v1.X * v2.Y - v1.Y * v2.X;

                current.Angle = Math.Abs(Math.Atan2(det, dot));
            }
        }

        private static Vector2 getEndCursorPosition(DiffObject o) => o.LazyEndPosition ?? o.StackedPosition;

        /// <summary>Ported OsuDifficultyHitObject.computeSliderCursorPosition: the "lazy" cursor path of a slider.</summary>
        private static void computeSliderCursorPosition(DiffObject d, ParsedBeatmap beatmap, HitObjectModel ho, Vector2 stackOffset)
        {
            double duration = ho.Duration;
            double spanDuration = duration / d.Slides;
            if (spanDuration <= 0)
                return;

            double pathLength = SliderGeometry.PathLength(ho.Path);
            double velocity = pathLength / spanDuration; // osu!pixels per millisecond
            double beatLength = beatLengthAt(beatmap, ho.StartTime);
            double scoringDistance = velocity * beatLength;
            double tickDistance = beatmap.SliderTickRate > 0 ? scoringDistance / beatmap.SliderTickRate : 0;
            double totalDistance = pathLength * d.Slides;

            var nested = generateNestedObjects(d, ho, stackOffset, spanDuration, velocity, tickDistance, totalDistance);

            double trackingEndTime = Math.Max(ho.StartTime + duration + tail_leniency, ho.StartTime + duration / 2);

            // If the last real tick falls after the tracking end time, move it to the end (matches lazer's quirk).
            int lastTickIndex = -1;
            for (int i = 0; i < nested.Count; i++)
                if (nested[i].IsTick)
                    lastTickIndex = i;

            if (lastTickIndex >= 0 && nested[lastTickIndex].Time > trackingEndTime)
            {
                trackingEndTime = nested[lastTickIndex].Time;
                var t = nested[lastTickIndex];
                nested.RemoveAt(lastTickIndex);
                nested.Add(t);
            }

            d.LazyTravelTime = trackingEndTime - ho.StartTime;

            double endTimeMin = d.LazyTravelTime / spanDuration;
            if (endTimeMin % 2 >= 1)
                endTimeMin = 1 - endTimeMin % 1;
            else
                endTimeMin %= 1;

            Vector2 lazyEnd = d.StackedPosition + relativePositionAt(ho.Path, endTimeMin);

            Vector2 currCursorPosition = d.StackedPosition;
            double scalingFactor = normalised_radius / d.Radius;

            for (int i = 1; i < nested.Count; i++)
            {
                Vector2 currMovement = nested[i].StackedPosition - currCursorPosition;
                double currMovementLength = scalingFactor * currMovement.Length;

                double requiredMovement = assumed_slider_radius;

                if (i == nested.Count - 1)
                {
                    Vector2 lazyMovement = lazyEnd - currCursorPosition;
                    if (lazyMovement.Length < currMovement.Length)
                        currMovement = lazyMovement;

                    currMovementLength = scalingFactor * currMovement.Length;
                }
                else if (nested[i].IsRepeat)
                {
                    requiredMovement = normalised_radius;
                }

                if (currMovementLength > requiredMovement)
                {
                    currCursorPosition += currMovement * (float)((currMovementLength - requiredMovement) / currMovementLength);
                    currMovementLength *= (currMovementLength - requiredMovement) / currMovementLength;
                    d.LazyTravelDistance += currMovementLength;
                }

                if (i == nested.Count - 1)
                    lazyEnd = currCursorPosition;
            }

            d.LazyEndPosition = lazyEnd;
        }

        /// <summary>Ported SliderEventGenerator: head, ticks, repeats and tail (LegacyLastTick excluded), time-ordered.</summary>
        private static List<NestedObject> generateNestedObjects(DiffObject d, HitObjectModel ho, Vector2 stackOffset,
                                                                double spanDuration, double velocity, double tickDistance, double totalDistance)
        {
            var result = new List<NestedObject>();
            int spanCount = d.Slides;

            double length = Math.Min(100000, totalDistance);
            tickDistance = Math.Clamp(tickDistance, 0, length);
            double minDistanceFromEnd = velocity * 10;

            Vector2 posAt(double progress) => d.StackedPosition + relativePositionAt(ho.Path, progress);

            // Head.
            result.Add(new NestedObject { Time = ho.StartTime, StackedPosition = d.StackedPosition });

            for (int span = 0; span < spanCount; span++)
            {
                double spanStartTime = ho.StartTime + span * spanDuration;
                bool reversed = span % 2 == 1;

                if (tickDistance != 0)
                {
                    var ticks = new List<NestedObject>();
                    for (double dist = tickDistance; dist <= length; dist += tickDistance)
                    {
                        if (dist >= length - minDistanceFromEnd)
                            break;

                        double pathProgress = dist / length;
                        double timeProgress = reversed ? 1 - pathProgress : pathProgress;

                        ticks.Add(new NestedObject
                        {
                            IsTick = true,
                            Time = spanStartTime + timeProgress * spanDuration,
                            StackedPosition = posAt(pathProgress),
                        });
                    }

                    if (reversed)
                        ticks.Reverse();

                    result.AddRange(ticks);
                }

                if (span < spanCount - 1)
                {
                    result.Add(new NestedObject
                    {
                        IsRepeat = true,
                        Time = spanStartTime + spanDuration,
                        StackedPosition = posAt((span + 1) % 2),
                    });
                }
            }

            // Tail (LegacyLastTick intentionally omitted - it is not part of NestedHitObjects).
            result.Add(new NestedObject
            {
                Time = ho.StartTime + spanCount * spanDuration,
                StackedPosition = posAt(spanCount % 2),
            });

            return result;
        }

        /// <summary>The beat length (ms per beat) of the uninherited (red) timing point active at <paramref name="time"/>.</summary>
        private static double beatLengthAt(ParsedBeatmap beatmap, double time)
        {
            var points = beatmap.BeatPoints;
            if (points.Count == 0)
                return 1000; // arbitrary fallback; only affects tick density.

            double beatLength = points[0].BeatLength;
            foreach (var p in points)
            {
                if (p.Time <= time)
                    beatLength = p.BeatLength;
                else
                    break;
            }

            return beatLength > 0 ? beatLength : 1000;
        }

        // ---------------------------------------------------------------------------------------------------
        // Data carriers.
        // ---------------------------------------------------------------------------------------------------

        private sealed class DiffObject
        {
            public HitObjectKind Kind;
            public double StartTime;
            public double EndTime;
            public double Radius;
            public Vector2 StackedPosition;

            public bool IsSlider;
            public int Slides = 1;
            public IReadOnlyList<Vector2> Path = null!;
            public Vector2 TailStackedPosition;
            public Vector2? LazyEndPosition;
            public double LazyTravelDistance;
            public double LazyTravelTime;

            public int Index;
            public double DeltaTime;
            public double StrainTime;
            public double LazyJumpDistance;
            public double MinimumJumpDistance;
            public double MinimumJumpTime;
            public double TravelDistance;
            public double TravelTime;
            public double? Angle;
            public double HitWindowGreat;

            public List<DiffObject> All = null!;

            public DiffObject? Previous(int backwardsIndex)
            {
                int i = Index - (backwardsIndex + 1);
                return i >= 0 && i < All.Count ? All[i] : null;
            }

            public DiffObject? Next(int forwardsIndex)
            {
                int i = Index + (forwardsIndex + 1);
                return i >= 0 && i < All.Count ? All[i] : null;
            }

            /// <summary>Returns how possible it is to doubletap this object with the next one (0..1).</summary>
            public double GetDoubletapness(DiffObject? next)
            {
                if (next != null)
                {
                    double currDeltaTime = Math.Max(1, DeltaTime);
                    double nextDeltaTime = Math.Max(1, next.DeltaTime);
                    double deltaDifference = Math.Abs(nextDeltaTime - currDeltaTime);
                    double speedRatio = currDeltaTime / Math.Max(currDeltaTime, deltaDifference);
                    double windowRatio = Math.Pow(Math.Min(1, currDeltaTime / HitWindowGreat), 2);
                    return 1.0 - Math.Pow(speedRatio, 1 - windowRatio);
                }

                return 0;
            }
        }

        private struct NestedObject
        {
            public bool IsTick;
            public bool IsRepeat;
            public double Time;
            public Vector2 StackedPosition;
        }

        // ---------------------------------------------------------------------------------------------------
        // Skills (ported StrainSkill / OsuStrainSkill / Aim / Speed).
        // ---------------------------------------------------------------------------------------------------

        private abstract class OsuStrainSkill
        {
            protected virtual int ReducedSectionCount => 10;
            protected virtual double ReducedStrainBaseline => 0.75;
            protected virtual double DecayWeight => 0.9;
            protected const int section_length = 400;

            private double currentSectionPeak;
            private double currentSectionEnd;
            private readonly List<double> strainPeaks = new List<double>();
            protected readonly List<double> ObjectStrains = new List<double>();

            protected abstract double StrainValueAt(DiffObject current);
            protected abstract double CalculateInitialStrain(double time, DiffObject current);

            public void Process(DiffObject current)
            {
                if (current.Index == 0)
                    currentSectionEnd = Math.Ceiling(current.StartTime / section_length) * section_length;

                while (current.StartTime > currentSectionEnd)
                {
                    strainPeaks.Add(currentSectionPeak);
                    currentSectionPeak = CalculateInitialStrain(currentSectionEnd, current);
                    currentSectionEnd += section_length;
                }

                double strain = StrainValueAt(current);
                currentSectionPeak = Math.Max(strain, currentSectionPeak);
                ObjectStrains.Add(strain);
            }

            private IEnumerable<double> getCurrentStrainPeaks() => strainPeaks.Append(currentSectionPeak);

            public double DifficultyValue()
            {
                double difficulty = 0;
                double weight = 1;

                List<double> strains = getCurrentStrainPeaks().Where(p => p > 0).OrderByDescending(p => p).ToList();

                for (int i = 0; i < Math.Min(strains.Count, ReducedSectionCount); i++)
                {
                    double scale = Math.Log10(lerp(1, 10, Math.Clamp((double)i / ReducedSectionCount, 0, 1)));
                    strains[i] *= lerp(ReducedStrainBaseline, 1.0, scale);
                }

                foreach (double strain in strains.OrderByDescending(s => s))
                {
                    difficulty += strain * weight;
                    weight *= DecayWeight;
                }

                return difficulty;
            }

            public static double DifficultyToPerformance(double difficulty) => Math.Pow(5.0 * Math.Max(1.0, difficulty / 0.0675) - 4.0, 3.0) / 100000.0;

            private static double lerp(double a, double b, double t) => a + (b - a) * t;
        }

        private sealed class AimSkill : OsuStrainSkill
        {
            private readonly bool includeSliders;
            private double currentStrain;
            private const double skill_multiplier = 25.6;
            private const double strain_decay_base = 0.15;

            public AimSkill(bool includeSliders) => this.includeSliders = includeSliders;

            private static double strainDecay(double ms) => Math.Pow(strain_decay_base, ms / 1000);

            protected override double CalculateInitialStrain(double time, DiffObject current)
                => currentStrain * strainDecay(time - current.Previous(0)!.StartTime);

            protected override double StrainValueAt(DiffObject current)
            {
                currentStrain *= strainDecay(current.DeltaTime);
                currentStrain += AimEvaluator.EvaluateDifficultyOf(current, includeSliders) * skill_multiplier;
                return currentStrain;
            }
        }

        private sealed class SpeedSkill : OsuStrainSkill
        {
            private double currentStrain;
            private double currentRhythm;
            private const double skill_multiplier = 1.46;
            private const double strain_decay_base = 0.3;

            protected override int ReducedSectionCount => 5;

            private static double strainDecay(double ms) => Math.Pow(strain_decay_base, ms / 1000);

            protected override double CalculateInitialStrain(double time, DiffObject current)
                => (currentStrain * currentRhythm) * strainDecay(time - current.Previous(0)!.StartTime);

            protected override double StrainValueAt(DiffObject current)
            {
                currentStrain *= strainDecay(current.StrainTime);
                currentStrain += SpeedEvaluator.EvaluateDifficultyOf(current) * skill_multiplier;
                currentRhythm = RhythmEvaluator.EvaluateDifficultyOf(current);
                return currentStrain * currentRhythm;
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Evaluators (ported AimEvaluator / SpeedEvaluator / RhythmEvaluator).
        // ---------------------------------------------------------------------------------------------------

        private static class AimEvaluator
        {
            private const double wide_angle_multiplier = 1.5;
            private const double acute_angle_multiplier = 2.6;
            private const double slider_multiplier = 1.35;
            private const double velocity_change_multiplier = 0.75;
            private const double wiggle_multiplier = 1.02;

            public static double EvaluateDifficultyOf(DiffObject current, bool withSliderTravelDistance)
            {
                if (current.Kind == HitObjectKind.Spinner || current.Index <= 1 || current.Previous(0)!.Kind == HitObjectKind.Spinner)
                    return 0;

                var osuCurrObj = current;
                var osuLastObj = current.Previous(0)!;
                var osuLastLastObj = current.Previous(1)!;

                const int radius = normalised_radius;
                const int diameter = normalised_diameter;

                double currVelocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;

                if (osuLastObj.IsSlider && withSliderTravelDistance)
                {
                    double travelVelocity = osuLastObj.TravelDistance / osuLastObj.TravelTime;
                    double movementVelocity = osuCurrObj.MinimumJumpDistance / osuCurrObj.MinimumJumpTime;
                    currVelocity = Math.Max(currVelocity, movementVelocity + travelVelocity);
                }

                double prevVelocity = osuLastObj.LazyJumpDistance / osuLastObj.StrainTime;

                if (osuLastLastObj.IsSlider && withSliderTravelDistance)
                {
                    double travelVelocity = osuLastLastObj.TravelDistance / osuLastLastObj.TravelTime;
                    double movementVelocity = osuLastObj.MinimumJumpDistance / osuLastObj.MinimumJumpTime;
                    prevVelocity = Math.Max(prevVelocity, movementVelocity + travelVelocity);
                }

                double wideAngleBonus = 0;
                double acuteAngleBonus = 0;
                double sliderBonus = 0;
                double velocityChangeBonus = 0;
                double wiggleBonus = 0;

                double aimStrain = currVelocity;

                if (Math.Max(osuCurrObj.StrainTime, osuLastObj.StrainTime) < 1.25 * Math.Min(osuCurrObj.StrainTime, osuLastObj.StrainTime))
                {
                    if (osuCurrObj.Angle != null && osuLastObj.Angle != null)
                    {
                        double currAngle = osuCurrObj.Angle.Value;
                        double lastAngle = osuLastObj.Angle.Value;

                        double angleBonus = Math.Min(currVelocity, prevVelocity);

                        wideAngleBonus = calcWideAngleBonus(currAngle);
                        acuteAngleBonus = calcAcuteAngleBonus(currAngle);

                        wideAngleBonus *= 1 - Math.Min(wideAngleBonus, Math.Pow(calcWideAngleBonus(lastAngle), 3));
                        acuteAngleBonus *= 0.08 + 0.92 * (1 - Math.Min(acuteAngleBonus, Math.Pow(calcAcuteAngleBonus(lastAngle), 3)));

                        wideAngleBonus *= angleBonus * Utils.Smootherstep(osuCurrObj.LazyJumpDistance, 0, diameter);

                        acuteAngleBonus *= angleBonus *
                                           Utils.Smootherstep(Utils.MillisecondsToBPM(osuCurrObj.StrainTime, 2), 300, 400) *
                                           Utils.Smootherstep(osuCurrObj.LazyJumpDistance, diameter, diameter * 2);

                        wiggleBonus = angleBonus
                                      * Utils.Smootherstep(osuCurrObj.LazyJumpDistance, radius, diameter)
                                      * Math.Pow(Utils.ReverseLerp(osuCurrObj.LazyJumpDistance, diameter * 3, diameter), 1.8)
                                      * Utils.Smootherstep(currAngle, double.DegreesToRadians(110), double.DegreesToRadians(60))
                                      * Utils.Smootherstep(osuLastObj.LazyJumpDistance, radius, diameter)
                                      * Math.Pow(Utils.ReverseLerp(osuLastObj.LazyJumpDistance, diameter * 3, diameter), 1.8)
                                      * Utils.Smootherstep(lastAngle, double.DegreesToRadians(110), double.DegreesToRadians(60));
                    }
                }

                if (Math.Max(prevVelocity, currVelocity) != 0)
                {
                    prevVelocity = (osuLastObj.LazyJumpDistance + osuLastLastObj.TravelDistance) / osuLastObj.StrainTime;
                    currVelocity = (osuCurrObj.LazyJumpDistance + osuLastObj.TravelDistance) / osuCurrObj.StrainTime;

                    double distRatio = Math.Pow(Math.Sin(Math.PI / 2 * Math.Abs(prevVelocity - currVelocity) / Math.Max(prevVelocity, currVelocity)), 2);

                    double overlapVelocityBuff = Math.Min(diameter * 1.25 / Math.Min(osuCurrObj.StrainTime, osuLastObj.StrainTime), Math.Abs(prevVelocity - currVelocity));

                    velocityChangeBonus = overlapVelocityBuff * distRatio;

                    velocityChangeBonus *= Math.Pow(Math.Min(osuCurrObj.StrainTime, osuLastObj.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj.StrainTime), 2);
                }

                if (osuLastObj.IsSlider)
                    sliderBonus = osuLastObj.TravelDistance / osuLastObj.TravelTime;

                aimStrain += wiggleBonus * wiggle_multiplier;

                aimStrain += Math.Max(acuteAngleBonus * acute_angle_multiplier, wideAngleBonus * wide_angle_multiplier + velocityChangeBonus * velocity_change_multiplier);

                if (withSliderTravelDistance)
                    aimStrain += sliderBonus * slider_multiplier;

                return aimStrain;
            }

            private static double calcWideAngleBonus(double angle) => Utils.Smoothstep(angle, double.DegreesToRadians(40), double.DegreesToRadians(140));
            private static double calcAcuteAngleBonus(double angle) => Utils.Smoothstep(angle, double.DegreesToRadians(140), double.DegreesToRadians(40));
        }

        private static class SpeedEvaluator
        {
            private const double single_spacing_threshold = normalised_diameter * 1.25;
            private const double min_speed_bonus = 200;
            private const double speed_balancing_factor = 40;
            private const double distance_multiplier = 0.9;

            public static double EvaluateDifficultyOf(DiffObject current)
            {
                if (current.Kind == HitObjectKind.Spinner)
                    return 0;

                var osuCurrObj = current;
                var osuPrevObj = current.Index > 0 ? current.Previous(0) : null;

                double strainTime = osuCurrObj.StrainTime;
                double doubletapness = 1.0 - osuCurrObj.GetDoubletapness(osuCurrObj.Next(0));

                strainTime /= Math.Clamp((strainTime / osuCurrObj.HitWindowGreat) / 0.93, 0.92, 1);

                double speedBonus = 0.0;

                if (Utils.MillisecondsToBPM(strainTime) > min_speed_bonus)
                    speedBonus = 0.75 * Math.Pow((Utils.BPMToMilliseconds(min_speed_bonus) - strainTime) / speed_balancing_factor, 2);

                double travelDistance = osuPrevObj?.TravelDistance ?? 0;
                double distance = travelDistance + osuCurrObj.MinimumJumpDistance;

                distance = Math.Min(distance, single_spacing_threshold);

                double distanceBonus = Math.Pow(distance / single_spacing_threshold, 3.95) * distance_multiplier;

                double difficulty = (1 + speedBonus + distanceBonus) * 1000 / strainTime;

                return difficulty * doubletapness;
            }
        }

        private static class RhythmEvaluator
        {
            private const int history_time_max = 5 * 1000;
            private const int history_objects_max = 32;
            private const double rhythm_overall_multiplier = 0.95;
            private const double rhythm_ratio_multiplier = 12.0;

            public static double EvaluateDifficultyOf(DiffObject current)
            {
                if (current.Kind == HitObjectKind.Spinner)
                    return 0;

                double rhythmComplexitySum = 0;
                double deltaDifferenceEpsilon = current.HitWindowGreat * 0.3;

                var island = new Island(deltaDifferenceEpsilon);
                var previousIsland = new Island(deltaDifferenceEpsilon);
                var islandCounts = new List<(Island Island, int Count)>();

                double startRatio = 0;
                bool firstDeltaSwitch = false;

                int historicalNoteCount = Math.Min(current.Index, history_objects_max);
                int rhythmStart = 0;

                while (rhythmStart < historicalNoteCount - 2 && current.StartTime - current.Previous(rhythmStart)!.StartTime < history_time_max)
                    rhythmStart++;

                DiffObject prevObj = current.Previous(rhythmStart)!;
                DiffObject lastObj = current.Previous(rhythmStart + 1)!;

                for (int i = rhythmStart; i > 0; i--)
                {
                    DiffObject currObj = current.Previous(i - 1)!;

                    double timeDecay = (history_time_max - (current.StartTime - currObj.StartTime)) / history_time_max;
                    double noteDecay = (double)(historicalNoteCount - i) / historicalNoteCount;
                    double currHistoricalDecay = Math.Min(noteDecay, timeDecay);

                    double currDelta = currObj.StrainTime;
                    double prevDelta = prevObj.StrainTime;
                    double lastDelta = lastObj.StrainTime;

                    double deltaDifferenceRatio = Math.Min(prevDelta, currDelta) / Math.Max(prevDelta, currDelta);
                    double currRatio = 1.0 + rhythm_ratio_multiplier * Math.Min(0.5, Math.Pow(Math.Sin(Math.PI / deltaDifferenceRatio), 2));

                    double fraction = Math.Max(prevDelta / currDelta, currDelta / prevDelta);
                    double fractionMultiplier = Math.Clamp(2.0 - fraction / 8.0, 0.0, 1.0);

                    double windowPenalty = Math.Min(1, Math.Max(0, Math.Abs(prevDelta - currDelta) - deltaDifferenceEpsilon) / deltaDifferenceEpsilon);

                    double effectiveRatio = windowPenalty * currRatio * fractionMultiplier;

                    if (firstDeltaSwitch)
                    {
                        if (Math.Abs(prevDelta - currDelta) < deltaDifferenceEpsilon)
                        {
                            island.AddDelta((int)currDelta);
                        }
                        else
                        {
                            if (currObj.IsSlider)
                                effectiveRatio *= 0.125;
                            if (prevObj.IsSlider)
                                effectiveRatio *= 0.3;

                            if (island.IsSimilarPolarity(previousIsland))
                                effectiveRatio *= 0.5;

                            if (lastDelta > prevDelta + deltaDifferenceEpsilon && prevDelta > currDelta + deltaDifferenceEpsilon)
                                effectiveRatio *= 0.125;

                            if (previousIsland.DeltaCount == island.DeltaCount)
                                effectiveRatio *= 0.5;

                            var islandCount = islandCounts.FirstOrDefault(x => x.Island.Equals(island));

                            if (islandCount != default)
                            {
                                int countIndex = islandCounts.IndexOf(islandCount);

                                if (previousIsland.Equals(island))
                                    islandCount.Count++;

                                double power = Utils.Logistic(island.Delta, maxValue: 2.75, multiplier: 0.24, midpointOffset: 58.33);
                                effectiveRatio *= Math.Min(3.0 / islandCount.Count, Math.Pow(1.0 / islandCount.Count, power));

                                islandCounts[countIndex] = (islandCount.Island, islandCount.Count);
                            }
                            else
                            {
                                islandCounts.Add((island, 1));
                            }

                            double doubletapness = prevObj.GetDoubletapness(currObj);
                            effectiveRatio *= 1 - doubletapness * 0.75;

                            rhythmComplexitySum += Math.Sqrt(effectiveRatio * startRatio) * currHistoricalDecay;

                            startRatio = effectiveRatio;
                            previousIsland = island;

                            if (prevDelta + deltaDifferenceEpsilon < currDelta)
                                firstDeltaSwitch = false;

                            island = new Island((int)currDelta, deltaDifferenceEpsilon);
                        }
                    }
                    else if (prevDelta > currDelta + deltaDifferenceEpsilon)
                    {
                        firstDeltaSwitch = true;

                        if (currObj.IsSlider)
                            effectiveRatio *= 0.6;
                        if (prevObj.IsSlider)
                            effectiveRatio *= 0.6;

                        startRatio = effectiveRatio;
                        island = new Island((int)currDelta, deltaDifferenceEpsilon);
                    }

                    lastObj = prevObj;
                    prevObj = currObj;
                }

                return Math.Sqrt(4 + rhythmComplexitySum * rhythm_overall_multiplier) / 2.0;
            }

            private class Island : IEquatable<Island>
            {
                private readonly double deltaDifferenceEpsilon;

                public Island(double epsilon) => deltaDifferenceEpsilon = epsilon;

                public Island(int delta, double epsilon)
                {
                    deltaDifferenceEpsilon = epsilon;
                    Delta = Math.Max(delta, min_delta_time);
                    DeltaCount++;
                }

                public int Delta { get; private set; } = int.MaxValue;
                public int DeltaCount { get; private set; }

                public void AddDelta(int delta)
                {
                    if (Delta == int.MaxValue)
                        Delta = Math.Max(delta, min_delta_time);
                    DeltaCount++;
                }

                public bool IsSimilarPolarity(Island other) => DeltaCount % 2 == other.DeltaCount % 2;

                public bool Equals(Island? other)
                {
                    if (other == null)
                        return false;
                    return Math.Abs(Delta - other.Delta) < deltaDifferenceEpsilon && DeltaCount == other.DeltaCount;
                }

                public override bool Equals(object? obj) => Equals(obj as Island);
                public override int GetHashCode() => DeltaCount;
            }
        }

        // ---------------------------------------------------------------------------------------------------
        // Ported DifficultyCalculationUtils helpers.
        // ---------------------------------------------------------------------------------------------------

        private static class Utils
        {
            public static double BPMToMilliseconds(double bpm, int delimiter = 4) => 60000.0 / delimiter / bpm;
            public static double MillisecondsToBPM(double ms, int delimiter = 4) => 60000.0 / (ms * delimiter);

            public static double Logistic(double x, double midpointOffset, double multiplier, double maxValue = 1) => maxValue / (1 + Math.Exp(multiplier * (midpointOffset - x)));

            public static double Smoothstep(double x, double start, double end)
            {
                x = Math.Clamp((x - start) / (end - start), 0.0, 1.0);
                return x * x * (3.0 - 2.0 * x);
            }

            public static double Smootherstep(double x, double start, double end)
            {
                x = Math.Clamp((x - start) / (end - start), 0.0, 1.0);
                return x * x * x * (x * (6.0 * x - 15.0) + 10.0);
            }

            public static double ReverseLerp(double x, double start, double end) => Math.Clamp((x - start) / (end - start), 0.0, 1.0);
        }
    }
}
