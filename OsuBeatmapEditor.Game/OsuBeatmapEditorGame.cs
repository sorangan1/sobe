using System.Drawing;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
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

            // When the window isn't focused (alt-tabbed away) there's nothing to look at, so cap the
            // update/draw loops hard. The default inactive cap still spends real CPU/GPU on a window the
            // user isn't watching; 20 Hz keeps audio/background work ticking for a fraction of the power.
            host.MaximumInactiveHz = 20;

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
            // One app-wide editor-settings instance, so a setting toggled in any screen's settings overlay is
            // seen everywhere at once (e.g. the desktop Discord Rich Presence component). Screens reuse this
            // when present instead of constructing their own.
            var editorSettings = new Screens.Edit.EditorSettings(host.Storage);
            deps.CacheAs(editorSettings);
            // App-wide skin manager: owns imported .osk skins and tracks the active one (EditorSettings.SkinName).
            // The playfield renderer and hitsound player resolve it to texture/sound hit objects with the user's skin.
            deps.CacheAs(new Skinning.SkinManager(host, editorSettings));
            // Local "git checkout HEAD" pointers for collab difficulties; no per-frame work, so not in the tree.
            deps.CacheAs(new Online.CollabSession(host.Storage));
            // On-disk cache of the user's saved patterns, so the gallery doesn't re-hit the backend each open.
            deps.CacheAs(new Online.PatternStore(host.Storage));
            // Shared, disk-cached texture store for remote images (osu! avatars/covers). One instance app-wide so
            // an image is fetched from the CDN once and reused across panels and restarts.
            deps.CacheAs(new UI.OnlineTextureStore(host));
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
                // Pushes mapping-time stats to the backend while logged in; no visual footprint.
                new StatsSync(),
                // Heartbeats online/editing presence to the backend while logged in; no visual footprint.
                new PresenceReporter(),
                // Polls for pending collab invites and toasts them while logged in; no visual footprint.
                new InviteWatcher(),
                // Behind the screens: catches scroll over empty space to drive global volume.
                new ScrollCatcher { Scrolled = volume.AdjustMaster },
                // Wraps the screens so any IHasTooltip drawable (control-point handles, toolbar chips, ...) can
                // show a hover tooltip; the container tracks the mouse pointer on its own.
                new TooltipContainer { RelativeSizeAxes = Axes.Both, Child = screenStack = new ScreenStack { RelativeSizeAxes = Axes.Both } },
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
