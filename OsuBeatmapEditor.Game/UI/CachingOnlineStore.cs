using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// An <see cref="IResourceStore{T}"/> that fetches images from URLs (the osu! avatar/cover CDN) but keeps a
    /// persistent on-disk copy, so the same avatar/cover is downloaded once and then served from disk on every
    /// later request — including across app restarts. Wraps a framework <see cref="OnlineStore"/> for the actual
    /// network fetch; on a cache miss it downloads, writes the bytes to disk, and returns them.
    /// </summary>
    public sealed class CachingOnlineStore : IResourceStore<byte[]>
    {
        // Re-fetch a cached file once it is older than this, so avatars/covers that change upstream don't stay
        // stale forever. Generous because these images change rarely and the win is avoiding constant re-downloads.
        private static readonly TimeSpan max_age = TimeSpan.FromDays(14);

        private readonly OnlineStore inner = new OnlineStore();
        private readonly string cacheDir;

        public CachingOnlineStore(string cacheDir)
        {
            this.cacheDir = cacheDir;
            try
            {
                Directory.CreateDirectory(cacheDir);
            }
            catch
            {
                // If the cache directory can't be created we silently fall back to network-only behaviour.
            }
        }

        public byte[] Get(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null!;

            if (tryReadCached(name, out var cached))
                return cached!;

            var bytes = inner.Get(name);
            if (bytes != null)
                tryWriteCached(name, bytes);
            return bytes!;
        }

        public async Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(name))
                return null!;

            if (tryReadCached(name, out var cached))
                return cached!;

            var bytes = await inner.GetAsync(name, cancellationToken).ConfigureAwait(false);
            if (bytes != null)
                tryWriteCached(name, bytes);
            return bytes!;
        }

        public Stream? GetStream(string name)
        {
            var bytes = Get(name);
            return bytes == null ? null : new MemoryStream(bytes);
        }

        public IEnumerable<string> GetAvailableResources() => Array.Empty<string>();

        private string pathFor(string name)
        {
            // SHA1 of the URL keeps filenames flat, fixed-length and filesystem-safe.
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(name));
            return Path.Combine(cacheDir, Convert.ToHexString(hash));
        }

        private bool tryReadCached(string name, out byte[]? bytes)
        {
            bytes = null;
            try
            {
                string path = pathFor(name);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length == 0)
                    return false;
                if (DateTime.UtcNow - info.LastWriteTimeUtc > max_age)
                    return false;

                bytes = File.ReadAllBytes(path);
                return bytes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void tryWriteCached(string name, byte[] bytes)
        {
            try
            {
                // Write to a temp file then move, so a half-written download is never read back as a valid cache hit.
                string path = pathFor(name);
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                // Caching is best-effort; a failed write just means the next request re-downloads.
            }
        }

        public void Dispose() => inner.Dispose();
    }
}
