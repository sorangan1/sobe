using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Screens.Edit;

namespace OsuBeatmapEditor.Game.Skinning
{
    /// <summary>
    /// Owns the user's imported osu! skins. Skins live as extracted folders under <c>storage/skins/&lt;name&gt;/</c>;
    /// importing a <c>.osk</c> (a zip) just unpacks it there. The active skin is selected by name in
    /// <see cref="EditorSettings.SkinName"/>; this manager keeps <see cref="Current"/> in sync with that setting,
    /// rebuilding (and disposing the previous) <see cref="Skin"/> whenever it changes. Cached app-wide so the
    /// playfield renderer and the hitsound player can resolve the active skin.
    /// </summary>
    public sealed class SkinManager : IDisposable
    {
        private readonly GameHost host;
        private readonly EditorSettings settings;
        private readonly string skinsRoot;

        /// <summary>Absolute path of the skins folder (so the UI can reveal it in the OS file manager).</summary>
        public string SkinsPath => skinsRoot;

        /// <summary>The active skin, or null when no skin is selected (built-in procedural look). Read-only to callers.</summary>
        public IBindable<Skin?> Current => current;

        private readonly Bindable<Skin?> current = new Bindable<Skin?>();

        public SkinManager(GameHost host, EditorSettings settings)
        {
            this.host = host;
            this.settings = settings;

            skinsRoot = host.Storage.GetFullPath("skins");
            Directory.CreateDirectory(skinsRoot);

            settings.SkinName.BindValueChanged(_ => rebuild(), true);
        }

        /// <summary>Folder names of all installed skins, alphabetical. Each is a valid value for <see cref="EditorSettings.SkinName"/>.</summary>
        public string[] AvailableSkins()
        {
            try
            {
                return Directory.GetDirectories(skinsRoot)
                                .Select(Path.GetFileName)
                                .Where(n => !string.IsNullOrEmpty(n))
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .ToArray()!;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>The result of an import: the installed skin's folder name on success, or an error message.</summary>
        public readonly record struct ImportResult(bool Success, string Message)
        {
            public static ImportResult Ok(string name) => new ImportResult(true, name);
            public static ImportResult Fail(string error) => new ImportResult(false, error);
        }

        /// <summary>
        /// Extracts a <c>.osk</c> archive into <c>storage/skins/</c> WITHOUT activating it. This is the slow part
        /// (unzipping textures/sounds) and is safe to run off the update thread; pair it with <see cref="Activate"/>
        /// back on the update thread. Re-importing a skin of the same name overwrites it. Never throws: any failure
        /// (missing file, bad zip, IO error) comes back as a failed <see cref="ImportResult"/> whose Message is the
        /// installed skin's folder name on success.
        /// </summary>
        public ImportResult ExtractOsk(string oskPath)
        {
            if (string.IsNullOrEmpty(oskPath) || !File.Exists(oskPath))
                return ImportResult.Fail("The .osk file could not be found.");

            string name = sanitize(Path.GetFileNameWithoutExtension(oskPath));
            if (name.Length == 0)
                return ImportResult.Fail("That skin has an invalid name.");

            string target = Path.Combine(skinsRoot, name);

            try
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);

                // .NET's extractor rejects entries that would escape the target directory (zip-slip), so this is
                // safe against malicious archives.
                ZipFile.ExtractToDirectory(oskPath, target);
            }
            catch (Exception e)
            {
                Logger.Log($"Skin import failed: {e}", level: LogLevel.Error);
                return ImportResult.Fail("That .osk couldn't be read (it may be corrupt).");
            }

            return ImportResult.Ok(name);
        }

        /// <summary>
        /// Makes the named (already-extracted) skin the active one. Must run on the update thread: it can rebuild
        /// the live <see cref="Current"/> skin, which screens react to (rebuilding hit objects). If it's already
        /// the current skin, rebuilds in place to pick up freshly-extracted files.
        /// </summary>
        public void Activate(string name)
        {
            if (settings.SkinName.Value == name)
                rebuild();
            else
                settings.SkinName.Value = name;
        }

        /// <summary>Convenience: <see cref="ExtractOsk"/> then <see cref="Activate"/> on success (caller's thread).</summary>
        public ImportResult ImportOsk(string oskPath)
        {
            var result = ExtractOsk(oskPath);
            if (result.Success)
                Activate(result.Message);
            return result;
        }

        /// <summary>Disposes the current skin and rebuilds it from the selected name (null when none / missing).</summary>
        private void rebuild()
        {
            string name = settings.SkinName.Value;
            Skin? built = null;

            if (!string.IsNullOrEmpty(name))
            {
                string path = Path.Combine(skinsRoot, name);
                if (Directory.Exists(path))
                {
                    try
                    {
                        built = new Skin(name, path, host);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"Skin '{name}' failed to load: {e}", level: LogLevel.Error);
                    }
                }
            }

            var previous = current.Value;
            current.Value = built;
            previous?.Dispose();
        }

        /// <summary>Strips path separators and other characters that can't appear in a folder name.</summary>
        private static string sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        public void Dispose() => current.Value?.Dispose();
    }
}
