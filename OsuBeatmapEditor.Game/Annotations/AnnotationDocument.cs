using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuBeatmapEditor.Game.Annotations
{
    /// <summary>
    /// A whole "Review" layer: the shareable, editor-only modding overlay for one difficulty. Serialized to a
    /// standalone <c>.sobemod</c> file (see <see cref="AnnotationSerializer"/>) that can be passed to another
    /// person and imported into our editor. Bound to the map by metadata so we can warn if it has changed since
    /// the layer was authored, but the annotations themselves anchor only to time + playfield position.
    /// </summary>
    public class AnnotationDocument
    {
        public const string FileFormat = "sobemod";
        public const int CurrentVersion = 1;

        public string Format { get; set; } = FileFormat;
        public int Version { get; set; } = CurrentVersion;

        // --- Map binding (for matching / mismatch warnings; not used to anchor annotations) ---

        /// <summary>The .osu file hash the layer was authored against (the difficulty's hash at author time).</summary>
        public string OsuFileHash { get; set; } = string.Empty;
        public int SetOnlineId { get; set; } = -1;
        public string Difficulty { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;

        /// <summary>The person who last saved this layer (default author for new notes), and their colour.</summary>
        public string Author { get; set; } = string.Empty;
        public string AuthorColor { get; set; } = "#FF66AB";
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<Annotation> Annotations { get; set; } = new List<Annotation>();

        /// <summary>
        /// Merges another layer's annotations into this one (used when importing a collaborator's file). Entries
        /// with an id already present are skipped, so re-importing the same file is idempotent. Returns how many
        /// new annotations were added.
        /// </summary>
        public int Merge(AnnotationDocument other)
        {
            var existing = new HashSet<string>(Annotations.Select(a => a.Id));
            int added = 0;
            foreach (var a in other.Annotations)
            {
                if (string.IsNullOrEmpty(a.Id) || existing.Add(a.Id))
                {
                    Annotations.Add(a);
                    added++;
                }
            }
            return added;
        }
    }
}
