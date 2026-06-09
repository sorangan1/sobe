using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A <see cref="LifetimeManagementContainer"/> that only realises/updates/draws children whose
    /// lifetime window (set from each object's <c>LifetimeStart</c>/<c>LifetimeEnd</c>) overlaps the current
    /// clock time - the same per-object lifetime culling osu!lazer's <c>HitObjectContainer</c> uses, so
    /// per-frame cost scales with on-screen objects rather than the whole map. Exposes simple add/remove.
    /// </summary>
    public partial class HitObjectLifetimeContainer : LifetimeManagementContainer
    {
        public void Add(Drawable drawable) => AddInternal(drawable);

        public void Remove(Drawable drawable) => RemoveInternal(drawable, true);

        public void Clear() => ClearInternal();
    }
}
