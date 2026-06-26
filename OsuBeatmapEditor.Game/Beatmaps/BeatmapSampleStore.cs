using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// A resource store over a beatmap set's OWN sample files, so the editor plays a map's custom hitsounds
    /// (the <c>.wav</c>/<c>.mp3</c>/<c>.ogg</c> packed in the <c>.osz</c>) instead of only the default skin —
    /// matching osu!lazer, where the beatmap skin is consulted before the default skin.
    ///
    /// Files live in osu!lazer's content-addressable store (named by hash); this maps a requested logical name
    /// (e.g. <c>soft-hitclap2.wav</c> or a custom <c>clap.wav</c>) → the set's filename→hash table → on-disk path.
    /// Wrapped by <c>AudioManager.GetSampleStore</c>, which appends the wav/mp3/ogg extensions, so lookups arrive
    /// here already suffixed. Returns null for anything the set doesn't contain, letting the skin fallback take over.
    /// </summary>
    public sealed class BeatmapSampleStore : IResourceStore<byte[]>
    {
        private readonly IReadOnlyDictionary<string, string> files; // filename (lower-case) → hash
        private readonly string dataDirectory;

        public BeatmapSampleStore(IReadOnlyDictionary<string, string> files, string dataDirectory)
        {
            this.files = files;
            this.dataDirectory = dataDirectory;
        }

        private string? resolvePath(string name)
        {
            if (string.IsNullOrEmpty(name) || !files.TryGetValue(name.ToLowerInvariant(), out string? hash))
                return null;
            return LazerFileStore.ResolvePath(dataDirectory, hash);
        }

        // Returns null for a missing resource (the framework's store convention), hence null! against the
        // non-nullable interface signature — same as osu!framework's own resource stores.
        public byte[] Get(string name)
        {
            string? path = resolvePath(name);
            return path != null ? File.ReadAllBytes(path) : null!;
        }

        public async Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            string? path = resolvePath(name);
            return path != null ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null!;
        }

        public Stream? GetStream(string name)
        {
            string? path = resolvePath(name);
            return path != null ? File.OpenRead(path) : null;
        }

        public IEnumerable<string> GetAvailableResources() => files.Keys.ToArray();

        public void Dispose() { }
    }
}
