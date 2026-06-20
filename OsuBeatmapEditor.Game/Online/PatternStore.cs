using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// A local, persisted cache of the logged-in user's Pattern Gallery: the pattern list, their collections,
    /// and each pattern's serialized content (fetched once, then kept on disk). The gallery renders from this
    /// instantly and only hits the backend when the cache is stale (a TTL) or after a local change - so opening
    /// the gallery repeatedly, or reopening it on every map, doesn't hammer the database. Cached at the game
    /// root (one per process), keyed by user so switching accounts starts clean. Mirrors <see cref="CollabSession"/>.
    /// </summary>
    public class PatternStore
    {
        private const string filename = "patterns-cache.json";

        /// <summary>How long the cached pattern list is considered fresh before a background refresh.</summary>
        public static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(5);

        private readonly Storage? storage;
        private CacheData data;

        public PatternStore(Storage? storage = null)
        {
            this.storage = storage;
            data = load();
        }

        /// <summary>One cached pattern: its summary plus (once fetched) its serialized content.</summary>
        public class CachedPattern
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public Guid? CollectionId { get; set; }
            public int ObjectCount { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }

            /// <summary>The serialized pattern; null until first fetched (then persisted).</summary>
            public string? Content { get; set; }

            /// <summary>True for a locally-saved pattern not yet confirmed by the server (kept across syncs).</summary>
            public bool Unsynced { get; set; }
        }

        private class CacheData
        {
            public long UserId { get; set; }
            public DateTimeOffset ListFetchedAt { get; set; }
            public List<PatternCollectionInfo> Collections { get; set; } = new List<PatternCollectionInfo>();
            public List<CachedPattern> Patterns { get; set; } = new List<CachedPattern>();
        }

        public IReadOnlyList<CachedPattern> Patterns => data.Patterns;
        public IReadOnlyList<PatternCollectionInfo> Collections => data.Collections;

        /// <summary>True when the cached list is older than <see cref="ListTtl"/> (or never fetched for this user).</summary>
        public bool IsStale => DateTimeOffset.UtcNow - data.ListFetchedAt > ListTtl;

        public string? Content(Guid id) => data.Patterns.FirstOrDefault(p => p.Id == id)?.Content;

        /// <summary>Resets the cache when the logged-in user changes (so we never show another account's patterns).</summary>
        public void EnsureUser(long userId)
        {
            if (data.UserId == userId)
                return;
            data = new CacheData { UserId = userId };
            persist();
        }

        /// <summary>
        /// Merges a freshly-fetched list + collections into the cache, keeping any cached content whose
        /// <see cref="CachedPattern.UpdatedAt"/> still matches (so unchanged patterns are never re-downloaded).
        /// </summary>
        public void SyncList(long userId, IReadOnlyList<PatternSummary> summaries, IReadOnlyList<PatternCollectionInfo> collections)
        {
            var prior = data.Patterns.ToDictionary(p => p.Id, p => p);
            data.UserId = userId;
            data.Collections = collections.ToList();
            var merged = summaries.Select(s => new CachedPattern
            {
                Id = s.Id,
                Name = s.Name,
                CollectionId = s.CollectionId,
                ObjectCount = s.ObjectCount,
                UpdatedAt = s.UpdatedAt,
                Content = prior.TryGetValue(s.Id, out var old) && old.UpdatedAt == s.UpdatedAt ? old.Content : null,
            }).ToList();

            // Keep optimistically-saved patterns the server hasn't confirmed yet, so a sync never drops them.
            var serverIds = summaries.Select(s => s.Id).ToHashSet();
            merged.AddRange(data.Patterns.Where(p => p.Unsynced && !serverIds.Contains(p.Id)));

            data.Patterns = merged;
            data.ListFetchedAt = DateTimeOffset.UtcNow;
            persist();
        }

        /// <summary>Adds a pattern locally with a temporary id (returned), pending background upload.</summary>
        public CachedPattern AddLocal(string name, Guid? collectionId, string content, int objectCount)
        {
            var p = new CachedPattern
            {
                Id = Guid.NewGuid(),
                Name = name,
                CollectionId = collectionId,
                ObjectCount = objectCount,
                UpdatedAt = DateTimeOffset.UtcNow,
                Content = content,
                Unsynced = true,
            };
            data.Patterns.Insert(0, p);
            persist();
            return p;
        }

        /// <summary>Swaps a local pattern's temporary id for its server id once the upload succeeds.</summary>
        public void ConfirmUpload(Guid tempId, Guid serverId)
        {
            var p = data.Patterns.FirstOrDefault(x => x.Id == tempId);
            if (p == null)
                return;
            p.Id = serverId;
            p.Unsynced = false;
            persist();
        }

        /// <summary>Stores a fetched pattern's content so it's never downloaded again (until it changes).</summary>
        public void SetContent(Guid id, string content)
        {
            var p = data.Patterns.FirstOrDefault(x => x.Id == id);
            if (p == null)
                return;
            p.Content = content;
            persist();
        }

        // --- Optimistic local updates (so a rename/move/delete doesn't need a server round-trip to show) ---

        public void Rename(Guid id, string name)
        {
            var p = data.Patterns.FirstOrDefault(x => x.Id == id);
            if (p == null)
                return;
            p.Name = name;
            persist();
        }

        public void Move(Guid id, Guid? collectionId)
        {
            var p = data.Patterns.FirstOrDefault(x => x.Id == id);
            if (p == null)
                return;
            p.CollectionId = collectionId;
            persist();
        }

        public void Remove(Guid id)
        {
            if (data.Patterns.RemoveAll(x => x.Id == id) > 0)
                persist();
        }

        private CacheData load()
        {
            if (storage == null || !storage.Exists(filename))
                return new CacheData();

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                return JsonSerializer.Deserialize<CacheData>(stream) ?? new CacheData();
            }
            catch
            {
                return new CacheData();
            }
        }

        private void persist()
        {
            if (storage == null)
                return;

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                JsonSerializer.Serialize(stream, data);
            }
            catch
            {
                // Best-effort: a lost cache just means the next open re-fetches from the backend.
            }
        }
    }
}
