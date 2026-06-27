using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Performance;

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

        /// <summary>
        /// As each object enters/leaves its lifetime window, realize/release its slider body so off-screen
        /// sliders don't keep their GPU vertex buffers resident (the editor's main playback memory leak: a long
        /// map's worth of slider geometry piled up in GPU memory). The body is rebuilt when an object scrolls
        /// back into view, so scrubbing stays correct.
        /// </summary>
        protected override void OnChildLifetimeBoundaryCrossed(LifetimeBoundaryCrossedEvent e)
        {
            base.OnChildLifetimeBoundaryCrossed(e);

            if (e.Child is not DrawableHitObject d)
                return;

            // Alive == between Start and End: crossing Start forward, or End backward.
            bool alive = (e.Kind == LifetimeBoundaryKind.Start) == (e.Direction == LifetimeBoundaryCrossingDirection.Forward);
            d.SetBodyRealized(alive);
        }
    }
}
