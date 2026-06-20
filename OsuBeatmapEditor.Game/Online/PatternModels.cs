using System;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>A saved pattern as listed by <c>/api/patterns/mine</c> (no content - just enough to show a card).</summary>
    public class PatternSummary
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>The collection (folder) this pattern is in, or null for ungrouped.</summary>
        public Guid? CollectionId { get; set; }

        /// <summary>How many hit objects the pattern holds (for a count badge).</summary>
        public int ObjectCount { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>A pattern with its full serialized content, from <c>/api/patterns/{id}</c> (for preview/paste).</summary>
    public class PatternContent
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Guid? CollectionId { get; set; }

        /// <summary>The serialized pattern (see <see cref="Beatmaps.PatternSerializer"/>).</summary>
        public string Content { get; set; } = string.Empty;

        public int ObjectCount { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>A pattern collection (folder), from <c>/api/pattern-collections/mine</c>.</summary>
    public class PatternCollectionInfo
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
