using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game.Statistics;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Pushes the user's mapping-time stats to the sobe backend while logged in: the running total plus
    /// per-map editing time (for the profile's uploaded-beatmaps view). Fired on login, on shutdown, and
    /// once an hour in between. The server keeps the maximum, so over-sending is harmless. No-op when
    /// logged out — login stays optional. Has no visual footprint.
    /// </summary>
    public partial class StatsSync : Drawable
    {
        /// <summary>How often (ms) to push the running total while logged in (login + shutdown push too).</summary>
        private const double interval_ms = 60 * 60 * 1000;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private double sincePush;
        private long lastPushedSeconds = -1;

        public StatsSync()
        {
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Push immediately whenever a session appears (login or restored-from-disk).
            if (auth != null)
                auth.User.BindValueChanged(u => { if (u.NewValue != null) push(force: true); }, true);
        }

        protected override void Update()
        {
            base.Update();

            if (auth == null || statistics == null)
                return;

            sincePush += Clock.ElapsedFrameTime;
            if (sincePush < interval_ms)
                return;

            sincePush = 0;
            push();
        }

        private void push(bool force = false)
        {
            if (auth == null || statistics == null || !auth.IsLoggedIn)
                return;

            long seconds = (long)(statistics.TotalActiveMs / 1000);
            if (!force && seconds == lastPushedSeconds)
                return;

            lastPushedSeconds = seconds;
            auth.SyncMappingSeconds(seconds);

            // Per-map breakdown for the profile's uploaded-beatmaps view.
            var maps = statistics.PerMapActiveMsSnapshot()
                .Select(kv => (Key: kv.Key, Seconds: (long)(kv.Value / 1000)))
                .Where(m => m.Seconds > 0)
                .ToList();
            if (maps.Count > 0)
                auth.SyncMapTimes(maps);
        }

        protected override void Dispose(bool isDisposing)
        {
            // Best-effort final flush on shutdown.
            push(force: true);
            base.Dispose(isDisposing);
        }
    }
}
