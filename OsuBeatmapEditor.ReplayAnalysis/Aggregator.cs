using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuBeatmapEditor.ReplayAnalysis
{
    /// <summary>Collects the per-gap measurements into normalised-distance bins and prints a calibration report.</summary>
    public sealed class Aggregator
    {
        private readonly double[] edges;
        private readonly int bins;

        private readonly List<double>[] aim;
        private readonly List<double>[] arc;
        private readonly List<double>[] overshoot;
        private readonly List<double> stackMove = new();
        private readonly List<double> sliderLeadMs = new();
        private readonly List<double> sliderLeadFrac = new();
        private readonly List<double> streamSpacing = new();
        private readonly List<double> streamAim = new();
        private readonly List<double> streamShake = new();

        public Aggregator(double[] binEdges)
        {
            edges = binEdges;
            bins = binEdges.Length - 1;
            aim = NewBuckets();
            arc = NewBuckets();
            overshoot = NewBuckets();
        }

        private List<double>[] NewBuckets()
        {
            var b = new List<double>[bins];
            for (int i = 0; i < bins; i++) b[i] = new List<double>();
            return b;
        }

        private int BinOf(double dNorm)
        {
            for (int i = 0; i < bins; i++)
                if (dNorm >= edges[i] && dNorm < edges[i + 1])
                    return i;
            return bins - 1;
        }

        public void AddAim(double dNorm, double fracOfRadius) => aim[BinOf(dNorm)].Add(fracOfRadius);
        public void AddArc(double dNorm, double fracOfDist) => arc[BinOf(dNorm)].Add(fracOfDist);
        public void AddOvershoot(double dNorm, double fracOfDist) => overshoot[BinOf(dNorm)].Add(fracOfDist);
        public void AddStack(double fracOfRadius) => stackMove.Add(fracOfRadius);
        public void AddSliderLead(double ms, double frac) { sliderLeadMs.Add(ms); sliderLeadFrac.Add(frac); }
        public void AddStreamSpacing(double dNorm) => streamSpacing.Add(dNorm);
        public void AddStreamAim(double fracOfRadius) => streamAim.Add(fracOfRadius);
        public void AddStreamShake(double fracOfRadius) => streamShake.Add(fracOfRadius);

        public void Report()
        {
            Console.WriteLine("Distance bins are osu! normalised units (one circle diameter = 100).");
            Console.WriteLine();

            printTable("AIM ERROR (fraction of circle radius, by incoming jump)", aim, "median", "p90");
            printTable("ARC / lateral bow (fraction of jump distance)", arc, "median", "p90");
            printTable("OVERSHOOT past target (fraction of jump distance)", overshoot, "median", "p90");

            Console.WriteLine("STACK movement (cursor travel across a stacked pair, fraction of radius)");
            Console.WriteLine($"  n={stackMove.Count,-6} median={Program.Median(stackMove):0.000}  p90={P(stackMove, 0.90):0.000}");
            Console.WriteLine();

            Console.WriteLine("SLIDER release lead (how early the cursor leaves the slider)");
            Console.WriteLine($"  n={sliderLeadMs.Count,-6} median={Program.Median(sliderLeadMs):0.0}ms  p90={P(sliderLeadMs, 0.90):0.0}ms" +
                              $"   median frac of duration={Program.Median(sliderLeadFrac):0.000}");
            Console.WriteLine();

            Console.WriteLine("STREAMS (runs of >=6 constant-spacing circles) - how a real cursor flows through them");
            Console.WriteLine($"  spacing (normalised) : n={streamSpacing.Count,-6} median={Program.Median(streamSpacing):0.0}  p10={P(streamSpacing, 0.10):0.0}  p90={P(streamSpacing, 0.90):0.0}");
            Console.WriteLine($"  aim vs note centre   : median={Program.Median(streamAim):0.000}  p90={P(streamAim, 0.90):0.000} x radius");
            Console.WriteLine($"  SHAKE (hi-freq dev)  : median={Program.Median(streamShake):0.000}  p90={P(streamShake, 0.90):0.000} x radius  (=> how much real streams actually wobble)");
            Console.WriteLine();

            suggest();
        }

        private void printTable(string title, List<double>[] buckets, params string[] _)
        {
            Console.WriteLine(title);
            Console.WriteLine($"  {"range",-14}{"n",-8}{"median",-10}{"p90",-10}");
            for (int i = 0; i < bins; i++)
            {
                var v = buckets[i];
                if (v.Count == 0)
                    continue;
                string range = edges[i + 1] == double.PositiveInfinity ? $"{edges[i]:0}+" : $"{edges[i]:0}-{edges[i + 1]:0}";
                Console.WriteLine($"  {range,-14}{v.Count,-8}{Program.Median(v),-10:0.000}{P(v, 0.90),-10:0.000}");
            }
            Console.WriteLine();
        }

        /// <summary>Reads the bin tables and prints concrete suggested constants for the editor's humanise model.</summary>
        private void suggest()
        {
            Console.WriteLine("==================== SUGGESTED CONSTANTS ====================");

            // Aim error: take the all-up median fraction of radius as the offset scale.
            double aimMedian = Program.Median(aim.SelectMany(x => x).ToList());
            Console.WriteLine($"aimError radius factor   ~ {aimMedian:0.00} x radius (current code: 0.30 x radius x rand)");

            // Arc onset: smallest distance bin where median lateral bow drops below ~5% of the jump marks the
            // boundary between "flowing/curved" (streams) and "straight" (jumps).
            Console.WriteLine($"flow (arc) cutoff        ~ {boundary(arc, 0.05)} normalised  (arc < 5% of jump above this; current flow edges 80-170)");

            // Overshoot onset: smallest distance where median overshoot rises above ~3% of the jump.
            Console.WriteLine($"overshoot onset          ~ {onset(overshoot, 0.03)} normalised  (overshoot > 3% of jump above this; current edges 400-850)");

            Console.WriteLine($"slider release lead      ~ {Program.Median(sliderLeadMs):0}ms / {Program.Median(sliderLeadFrac):0.00} of duration (current: 0.15, <=70ms)");
            Console.WriteLine($"stack movement           ~ {Program.Median(stackMove):0.00} x radius (target ~0; current hold + 15% jitter)");
            Console.WriteLine("============================================================");
        }

        /// <summary>First bin (its lower edge) at/above which the median metric stays below <paramref name="threshold"/>.</summary>
        private string boundary(List<double>[] buckets, double threshold)
        {
            for (int i = 0; i < bins; i++)
            {
                if (buckets[i].Count < 5) continue;
                if (Program.Median(buckets[i]) < threshold)
                    return $"{edges[i]:0}";
            }
            return "n/a";
        }

        /// <summary>First bin (its lower edge) at/above which the median metric exceeds <paramref name="threshold"/>.</summary>
        private string onset(List<double>[] buckets, double threshold)
        {
            for (int i = 0; i < bins; i++)
            {
                if (buckets[i].Count < 5) continue;
                if (Program.Median(buckets[i]) > threshold)
                    return $"{edges[i]:0}";
            }
            return "n/a";
        }

        private static double P(List<double> values, double q)
        {
            if (values.Count == 0)
                return 0;
            var sorted = values.OrderBy(v => v).ToList();
            int idx = (int)Math.Clamp(Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
