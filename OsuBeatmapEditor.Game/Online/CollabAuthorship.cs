using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Per-object authorship for a collab, derived from its revision history: an object (keyed by its start
    /// time) is attributed to the author of the earliest revision in which an object at that time appears. This
    /// is an approximation - moving an object in time re-attributes it - but it's enough to colour the playfield
    /// by "who placed what". Built by pulling each revision's .osu text once, in ascending order.
    /// </summary>
    public class CollabAuthorship
    {
        /// <summary>Object start time (rounded to ms) -> osu! id of the author who first introduced it.</summary>
        public Dictionary<int, long> TimeToAuthor { get; } = new Dictionary<int, long>();

        /// <summary>Authors that contributed at least one object, in first-contribution order: (id, username).</summary>
        public List<(long Id, string Name)> Authors { get; } = new List<(long, string)>();

        /// <summary>Author id -> username, for everyone seen in the history (even with no surviving objects).</summary>
        public Dictionary<long, string> Names { get; } = new Dictionary<long, string>();

        /// <summary>The author who first placed an object at the given start time, or null if unknown.</summary>
        public long? AuthorAt(double startTime) =>
            TimeToAuthor.TryGetValue((int)Math.Round(startTime), out long id) ? id : (long?)null;

        /// <summary>
        /// Builds the attribution by walking the revision history oldest-first. Returns null if the collab has
        /// no revisions or the history couldn't be fetched.
        /// </summary>
        public static async Task<CollabAuthorship?> BuildAsync(string token, Guid collabId)
        {
            var summaries = await SobeApi.GetRevisionsAsync(token, collabId).ConfigureAwait(false);
            if (summaries.Count == 0)
                return null;

            var result = new CollabAuthorship();
            var contributed = new HashSet<long>();

            foreach (var summary in summaries.OrderBy(s => s.Number))
            {
                result.Names[summary.AuthorId] = summary.AuthorUsername ?? "?";

                var content = await SobeApi.PullRevisionAsync(token, collabId, summary.Number).ConfigureAwait(false);
                if (content == null || string.IsNullOrEmpty(content.OsuText))
                    continue;

                foreach (int time in parseObjectTimes(content.OsuText))
                {
                    if (result.TimeToAuthor.ContainsKey(time))
                        continue;

                    result.TimeToAuthor[time] = summary.AuthorId;
                    if (contributed.Add(summary.AuthorId))
                        result.Authors.Add((summary.AuthorId, summary.AuthorUsername ?? "?"));
                }
            }

            return result.TimeToAuthor.Count > 0 ? result : null;
        }

        // Yields each [HitObjects] line's start time (field 3, 0-based index 2). Tolerant of junk/blank lines.
        private static IEnumerable<int> parseObjectTimes(string osuText)
        {
            bool inObjects = false;
            foreach (var rawLine in osuText.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;
                if (line[0] == '[')
                {
                    inObjects = line.Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inObjects)
                    continue;

                string[] parts = line.Split(',');
                if (parts.Length < 3)
                    continue;
                if (int.TryParse(parts[2], out int time))
                    yield return time;
            }
        }
    }
}
