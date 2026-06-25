using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Wraps a texture-loader store and shrinks any decoded image whose largest side exceeds
    /// <see cref="maxDimension"/> before it reaches the GPU. Song backgrounds are routinely 1920x1080 (or
    /// larger), but the carousel only ever shows them in small cards, so uploading them at full resolution
    /// wastes a few MB of VRAM each. Downscaling once, on the (background) texture-load thread, keeps the
    /// carousel's memory footprint a fraction of the original with no visible quality loss at card size.
    /// On any failure it returns the original upload untouched, so it can never break image loading.
    /// </summary>
    internal class DownscalingTextureLoaderStore : IResourceStore<TextureUpload>
    {
        private readonly IResourceStore<TextureUpload> source;
        private readonly int maxDimension;

        public DownscalingTextureLoaderStore(IResourceStore<TextureUpload> source, int maxDimension)
        {
            this.source = source;
            this.maxDimension = maxDimension;
        }

        public TextureUpload Get(string name) => downscale(source.Get(name));

        public async Task<TextureUpload> GetAsync(string name, CancellationToken cancellationToken = default)
            => downscale(await source.GetAsync(name, cancellationToken).ConfigureAwait(false));

        public Stream GetStream(string name) => source.GetStream(name);

        public IEnumerable<string> GetAvailableResources() => source.GetAvailableResources();

        private TextureUpload downscale(TextureUpload upload)
        {
            if (upload == null)
                return upload!;

            int max = System.Math.Max(upload.Width, upload.Height);
            if (max <= maxDimension)
                return upload;

            try
            {
                float scale = (float)maxDimension / max;
                int width = System.Math.Max(1, (int)System.Math.Round(upload.Width * scale));
                int height = System.Math.Max(1, (int)System.Math.Round(upload.Height * scale));

                // Reconstruct the image from the decoded pixels, resize, and hand back a fresh upload. The
                // original (full-resolution) upload is no longer needed once copied, so release it.
                var image = Image.LoadPixelData<Rgba32>(upload.Data, upload.Width, upload.Height);
                image.Mutate(c => c.Resize(width, height));
                upload.Dispose();
                return new TextureUpload(image);
            }
            catch
            {
                // Any decode/resize hiccup: keep the original upload rather than dropping the image.
                return upload;
            }
        }

        public void Dispose() => source.Dispose();
    }
}
