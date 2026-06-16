using System.Drawing;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuBeatmapEditor.Game.Online;
using OsuBeatmapEditor.Game.Screens.SongSelect;
using OsuBeatmapEditor.Game.Statistics;
using OsuBeatmapEditor.Game.UI;
using OsuBeatmapEditor.Game.Updates;

namespace OsuBeatmapEditor.Game
{
    public partial class OsuBeatmapEditorGame : OsuBeatmapEditorGameBase
    {
        // Marker written after the first launch; its absence means a fresh install (used to seed defaults).
        private const string first_run_marker = "first-run.marker";

        private ScreenStack screenStack = null!;

        // Shared, app-wide transient-notification layer. Cached so any screen can push toasts.
        [Cached]
        private readonly ToastOverlay toasts = new ToastOverlay();

        // App-wide usage statistics (editor-open / active-editing time). Created and cached so any screen
        // can read it; added to the tree below so it accumulates time every frame.
        private StatisticsTracker statistics = null!;

        // App-wide self-update mechanism. Cached so the main menu/settings can drive it; added to the tree so
        // its scheduler can marshal background download/check work back onto the update thread.
        private UpdateManager updates = null!;

        // App-wide osu! login session (optional). Cached so any screen can read the current user; added to
        // the tree so its scheduler can marshal the browser-login callback back onto the update thread.
        private AuthManager auth = null!;

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            if (host.Window != null)
            {
                host.Window.Title = AppInfo.FullName;
                // Enforce a usable minimum editor size; the window may be larger.
                host.Window.MinSize = new Size(1280, 720);
            }
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var deps = new DependencyContainer(base.CreateChildDependencies(parent));
            statistics = new StatisticsTracker(parent.Get<GameHost>().Storage);
            deps.CacheAs(statistics);
            updates = new UpdateManager();
            deps.CacheAs(updates);
            var host = parent.Get<GameHost>();
            auth = new AuthManager(host.Storage, host);
            deps.CacheAs(auth);
            return deps;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var volume = new VolumeControl();

            Children = new Drawable[]
            {
                // Tracks usage time every frame; no visual footprint.
                statistics,
                // Drives self-update checks/downloads; no visual footprint.
                updates,
                // Holds the (optional) osu! login session; no visual footprint.
                auth,
                // Behind the screens: catches scroll over empty space to drive global volume.
                new ScrollCatcher { Scrolled = volume.AdjustMaster },
                screenStack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                // In front of everything so the popup is always visible.
                volume,
                // Above even the volume popup so action confirmations are never hidden.
                toasts,
            };
        }

        /// <summary>On the very first launch, seed the master volume to 50% (the framework persists it from
        /// there on). A marker file makes this a one-time action so the user's later choices are respected.
        /// Runs in LoadComplete because the base <see cref="osu.Framework.Game.Audio"/> manager isn't injectable
        /// at the game root's BDL (the Game creates it during its own load).</summary>
        private void applyFirstRunDefaults()
        {
            var storage = Host.Storage;
            if (storage.Exists(first_run_marker))
                return;

            Audio.Volume.Value = 0.5;

            try
            {
                using var stream = storage.GetStream(first_run_marker, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(AppInfo.Version);
            }
            catch
            {
                // If we can't write the marker we'll just seed the volume again next launch - harmless.
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            applyFirstRunDefaults();
            screenStack.Push(new SongSelectScreen());
        }
    }
}
