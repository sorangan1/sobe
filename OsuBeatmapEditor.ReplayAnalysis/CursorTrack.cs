using System;
using System.Collections.Generic;
using osuTK;

namespace OsuBeatmapEditor.ReplayAnalysis
{
    /// <summary>Time-indexed cursor samples with linear interpolation, for querying the cursor position at any ms.</summary>
    public sealed class CursorTrack
    {
        private readonly IReadOnlyList<ReplayFrame> frames;

        public CursorTrack(IReadOnlyList<ReplayFrame> frames) => this.frames = frames;

        public double StartTime => frames.Count > 0 ? frames[0].Time : 0;
        public double EndTime => frames.Count > 0 ? frames[^1].Time : 0;
        public int Count => frames.Count;

        /// <summary>Interpolated cursor position (osu!pixels) at <paramref name="time"/>, clamped to the recorded range.</summary>
        public Vector2 PositionAt(double time)
        {
            if (frames.Count == 0)
                return Vector2.Zero;
            if (time <= frames[0].Time)
                return new Vector2(frames[0].X, frames[0].Y);
            if (time >= frames[^1].Time)
                return new Vector2(frames[^1].X, frames[^1].Y);

            // Binary search for the last frame at/before time.
            int lo = 0, hi = frames.Count - 1, idx = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].Time <= time) { idx = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            var a = frames[idx];
            var b = frames[Math.Min(idx + 1, frames.Count - 1)];
            double span = b.Time - a.Time;
            float f = span > 0 ? (float)((time - a.Time) / span) : 0;
            return new Vector2(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
        }

        /// <summary>Total path length the cursor travelled over [from, to] (osu!pixels), summed over recorded frames.</summary>
        public float PathLength(double from, double to)
        {
            float len = 0;
            Vector2 prev = PositionAt(from);
            foreach (var fr in frames)
            {
                if (fr.Time <= from) continue;
                if (fr.Time >= to) break;
                var p = new Vector2(fr.X, fr.Y);
                len += (p - prev).Length;
                prev = p;
            }
            len += (PositionAt(to) - prev).Length;
            return len;
        }
    }
}
