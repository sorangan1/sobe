using System;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>A collab the current user belongs to, as listed by <c>/api/collabs/mine</c>.</summary>
    public class CollabSummary
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        /// <summary>The tip revision number on the server (0 = no revisions pushed yet).</summary>
        public int HeadRevision { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>"owner" or "editor".</summary>
        public string Role { get; set; } = "editor";

        /// <summary>Highest revision this user has pulled, or null if never.</summary>
        public int? LastSeenRevision { get; set; }

        public long OwnerId { get; set; }

        public string? OwnerUsername { get; set; }

        /// <summary>True when the server tip is ahead of what this user has pulled (changes available).</summary>
        public bool HasUnseen => HeadRevision > (LastSeenRevision ?? 0);

        /// <summary>True before the first-ever pull: the user still needs to bootstrap ("clone") the set.</summary>
        public bool NeedsBootstrap => LastSeenRevision is null;
    }

    /// <summary>The current tip of a collab, from <c>/api/collabs/{id}/head</c> (cheap to poll).</summary>
    public class CollabHead
    {
        public int HeadRevision { get; set; }

        /// <summary>Metadata of the tip revision, or null when the collab has no revisions yet.</summary>
        public CollabHeadRevision? Head { get; set; }
    }

    /// <summary>Tip-revision metadata (no .osu text) carried by <see cref="CollabHead"/>.</summary>
    public class CollabHeadRevision
    {
        public int Number { get; set; }

        public long AuthorId { get; set; }

        public string? Message { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string ContentHash { get; set; } = string.Empty;
    }

    /// <summary>A full revision including its .osu text, from <c>/api/collabs/{id}/revisions/{n}</c>.</summary>
    public class CollabRevisionContent
    {
        public int Number { get; set; }

        public long AuthorId { get; set; }

        public string OsuText { get; set; } = string.Empty;

        public string ContentHash { get; set; } = string.Empty;

        public string? Message { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>Metadata for a collab asset (audio/background) from <c>/api/collabs/{id}/assets</c>.</summary>
    public class CollabAssetInfo
    {
        public string Kind { get; set; } = string.Empty;

        public string Filename { get; set; } = string.Empty;

        public string Hash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Outcome of a push. <see cref="Ok"/> with <see cref="Number"/> on success; <see cref="Conflict"/> means
    /// the push was not a fast-forward (someone else pushed first) and <see cref="HeadRevision"/> is the server
    /// tip the client must pull/merge onto before retrying. <see cref="NoOp"/> = content identical to the tip.
    /// </summary>
    public readonly record struct CollabPushResult(bool Ok, int Number, bool Conflict, int HeadRevision, bool NoOp);
}
