using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Skinning
{
    /// <summary>
    /// A single imported osu! skin, loaded from an extracted <c>.osk</c> folder. Exposes its parsed
    /// <see cref="SkinConfiguration"/>, a texture lookup that mirrors osu!'s naming (with <c>@2x</c> high-res and
    /// <c>.png</c> resolution), and the raw resource store so callers can build a hitsound sample store over the
    /// same folder. Every element is optional: <see cref="GetTexture"/> returns null when a skin doesn't ship a
    /// given piece, and the renderer falls back to its procedural drawing for that element.
    /// </summary>
    public sealed class Skin : IDisposable
    {
        /// <summary>Folder name the skin was imported under; also its identifier in settings.</summary>
        public string Name { get; }

        /// <summary>Friendly name from <c>skin.ini</c>, falling back to the folder name.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(Config.Name) ? Name : Config.Name;

        public SkinConfiguration Config { get; }

        /// <summary>Raw byte store over the skin folder; used to build a hitsound sample store (<c>audio.GetSampleStore</c>).</summary>
        public IResourceStore<byte[]> Resources { get; }

        private readonly Storage storage;
        private readonly TextureStore textures;
        private readonly Dictionary<string, Texture?> textureCache = new Dictionary<string, Texture?>();

        public Skin(string name, string path, GameHost host)
        {
            Name = name;
            storage = new NativeStorage(path, host);

            Config = storage.Exists("skin.ini")
                ? SkinConfiguration.Parse(storage.GetStream("skin.ini"))
                : new SkinConfiguration { Name = name };

            Resources = new StorageBackedResourceStore(storage);
            textures = new TextureStore(host.Renderer, host.CreateTextureLoaderStore(Resources));
        }

        /// <summary>
        /// Looks up a skin texture by its osu! element name (e.g. <c>hitcircle</c>, <c>default-3</c>,
        /// <c>approachcircle</c>). Prefers the high-res <c>@2x</c> variant, then the plain <c>.png</c>. Returns null
        /// (cached) when the skin doesn't include that element, so the caller can fall back to procedural drawing.
        /// </summary>
        public Texture? GetTexture(string name)
        {
            if (textureCache.TryGetValue(name, out var cached))
                return cached;

            Texture? texture = load($"{name}@2x.png") ?? load($"{name}.png");
            textureCache[name] = texture;
            return texture;
        }

        private Texture? load(string filename)
        {
            // Guard on existence so a missing element is a cheap null instead of a thrown/loaded-and-logged miss.
            if (!storage.Exists(filename))
                return null;

            try
            {
                return textures.Get(filename);
            }
            catch (Exception e)
            {
                Logger.Log($"Skin texture '{filename}' failed to load: {e.Message}", level: LogLevel.Debug);
                return null;
            }
        }

        public void Dispose()
        {
            textures.Dispose();
            Resources.Dispose();
        }
    }
}
