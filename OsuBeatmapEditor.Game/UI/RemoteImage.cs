using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// App-wide shared texture store for remote images (osu! avatars/covers). Distinct subclass so it can be
    /// cached in DI without colliding with the framework's own <see cref="TextureStore"/>; backed by a
    /// disk-caching online store so an image is fetched from the CDN once and reused across panels and restarts.
    /// </summary>
    public class OnlineTextureStore : TextureStore
    {
        public OnlineTextureStore(GameHost host)
            : base(host.Renderer, host.CreateTextureLoaderStore(
                new CachingOnlineStore(Path.Combine(host.Storage.GetFullPath("."), "online-image-cache"))))
        {
        }
    }

    /// <summary>
    /// An image fetched from a URL (e.g. an osu! avatar/cover from the CDN). The texture is resolved in the
    /// background dependency loader — run on the framework's async load thread — so adding it through
    /// <c>LoadComponentAsync</c> loads off the update thread and shows the picture the moment it's fetched
    /// (the reliable path; a raw <c>Task</c> plus a manual store access can drop the GPU upload and leave the
    /// image blank).
    /// </summary>
    public partial class RemoteImage : CompositeDrawable
    {
        private readonly string url;
        private readonly TextureStore store;

        public RemoteImage(string url, TextureStore store)
        {
            this.url = url;
            this.store = store;
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var texture = store.Get(url);
            if (texture == null)
                return;

            InternalChild = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Texture = texture,
            };
        }
    }
}
