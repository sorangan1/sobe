using System;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Annotations
{
    /// <summary>
    /// Persists Review layers locally (under <c>annotations/</c> in app storage, keyed by a stable per-difficulty
    /// key so the layer survives saves that re-hash the .osu) and reads/writes the standalone <c>.sobemod</c>
    /// share file. Pure I/O - no UI, no editor state.
    /// </summary>
    public class AnnotationStore
    {
        public const string FileExtension = ".sobemod";
        private const string directory = "annotations";

        private readonly Storage? storage;

        public AnnotationStore(Storage? storage)
        {
            this.storage = storage;
        }

        /// <summary>
        /// A filesystem-safe key identifying a difficulty independent of its (changing) .osu hash: derived from the
        /// set's stable identity (artist|title|author) plus the difficulty name.
        /// </summary>
        public static string KeyFor(string setIdentity, string difficultyName)
        {
            string raw = $"{setIdentity}|{difficultyName}";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
            return sb.ToString();
        }

        private string localPath(string key) => Path.Combine(directory, key + FileExtension);

        /// <summary>Loads the locally-stored layer for the given key, or null if none / unreadable.</summary>
        public AnnotationDocument? LoadLocal(string key)
        {
            if (storage == null)
                return null;

            try
            {
                string path = localPath(key);
                if (!storage.Exists(path))
                    return null;

                using var stream = storage.GetStream(path, FileAccess.Read, FileMode.Open);
                using var reader = new StreamReader(stream);
                return AnnotationSerializer.Deserialize(reader.ReadToEnd());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Saves the layer to local storage under the given key (no-op if there's nothing to keep).</summary>
        public void SaveLocal(string key, AnnotationDocument doc)
        {
            if (storage == null)
                return;

            try
            {
                string path = localPath(key);

                // An empty layer that was never created shouldn't litter storage; delete any stale file instead.
                if (doc.Annotations.Count == 0)
                {
                    if (storage.Exists(path))
                        storage.Delete(path);
                    return;
                }

                using var stream = storage.GetStream(path, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(AnnotationSerializer.Serialize(doc));
            }
            catch
            {
                // Persistence is best-effort; a failed write shouldn't break saving the map.
            }
        }

        /// <summary>Writes the layer to a standalone share file. Returns the path, or null on failure.</summary>
        public static string? ExportToFile(AnnotationDocument doc, string exportsDir, string fileName)
        {
            try
            {
                Directory.CreateDirectory(exportsDir);
                string safe = string.Concat(fileName.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_'));
                string path = Path.Combine(exportsDir, safe + FileExtension);
                File.WriteAllText(path, AnnotationSerializer.Serialize(doc));
                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Reads a share file from disk, or null if it isn't a valid layer.</summary>
        public static AnnotationDocument? ImportFromFile(string path)
        {
            try
            {
                return File.Exists(path) ? AnnotationSerializer.Deserialize(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
