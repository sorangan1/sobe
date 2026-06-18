using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// One difficulty's link to a server collab: which collab it belongs to and the revision the local copy is
    /// based on (its "merge base" for the fast-forward push check).
    /// </summary>
    public class CollabLink
    {
        public Guid CollabId { get; set; }

        /// <summary>The revision number the local .osu is based on; a push must fast-forward from here.</summary>
        public int BaseRevision { get; set; }
    }

    /// <summary>
    /// Local, persisted record of which open difficulties are collabs and which revision each is based on. Keyed
    /// by the same stable map key the statistics tracker uses (artist|title|author|difficulty), so it survives the
    /// .osu hash changing on every save. This is the client side of the "git remote": the server holds the
    /// revisions; this file is the local checkout's HEAD pointer. Cached at the game root, read from any screen.
    /// </summary>
    public class CollabSession
    {
        private const string filename = "collabs.json";

        private readonly Storage? storage;
        private readonly Dictionary<string, CollabLink> links;

        public CollabSession(Storage? storage = null)
        {
            this.storage = storage;
            links = load();
        }

        /// <summary>The collab link for a map key, or null if that diff isn't a collab.</summary>
        public CollabLink? Get(string mapKey) =>
            links.TryGetValue(mapKey, out var link) ? link : null;

        public bool IsLinked(string mapKey) => links.ContainsKey(mapKey);

        /// <summary>True if any local difficulty is linked to this collab (i.e. it's already been bootstrapped).</summary>
        public bool IsLinkedTo(Guid collabId)
        {
            foreach (var l in links.Values)
            {
                if (l.CollabId == collabId)
                    return true;
            }
            return false;
        }

        /// <summary>The map key linked to this collab, or null if none is (it hasn't been bootstrapped here).</summary>
        public string? KeyForCollab(Guid collabId)
        {
            foreach (var kv in links)
            {
                if (kv.Value.CollabId == collabId)
                    return kv.Key;
            }
            return null;
        }

        /// <summary>Links a diff to a collab at the given base revision (called when creating or cloning a collab).</summary>
        public void Link(string mapKey, Guid collabId, int baseRevision)
        {
            links[mapKey] = new CollabLink { CollabId = collabId, BaseRevision = baseRevision };
            persist();
        }

        /// <summary>Advances the recorded base revision after a successful push or pull.</summary>
        public void SetBaseRevision(string mapKey, int revision)
        {
            if (links.TryGetValue(mapKey, out var link))
            {
                link.BaseRevision = revision;
                persist();
            }
        }

        public void Unlink(string mapKey)
        {
            if (links.Remove(mapKey))
                persist();
        }

        private Dictionary<string, CollabLink> load()
        {
            if (storage == null || !storage.Exists(filename))
                return new Dictionary<string, CollabLink>();

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                return JsonSerializer.Deserialize<Dictionary<string, CollabLink>>(stream)
                       ?? new Dictionary<string, CollabLink>();
            }
            catch
            {
                return new Dictionary<string, CollabLink>();
            }
        }

        private void persist()
        {
            if (storage == null)
                return;

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                JsonSerializer.Serialize(stream, links);
            }
            catch
            {
                // Best-effort; a lost link just means the user re-opens the collab from the menu next time.
            }
        }
    }
}
