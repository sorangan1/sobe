using osu.Framework.Allocation;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game.Statistics;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Heartbeats the user's presence to the sobe backend while logged in: "editing" (with the current map)
    /// when actively mapping, otherwise "online". Sends on a fixed interval and immediately whenever the
    /// activity changes, so the web shows live status. No-op when logged out. No visual footprint.
    /// </summary>
    public partial class PresenceReporter : Drawable
    {
        /// <summary>Heartbeat cadence (ms) — comfortably inside the server's 2-minute online window.</summary>
        private const double interval_ms = 25_000;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        private double sinceBeat;
        private string? lastState;
        private string? lastMap;

        public PresenceReporter()
        {
            AlwaysPresent = true;
        }

        protected override void Update()
        {
            base.Update();

            if (auth == null || !auth.IsLoggedIn)
                return;

            string state = statistics?.IsActivelyEditing == true ? "editing" : "online";
            string? map = state == "editing" ? statistics?.CurrentMapDisplay : null;

            sinceBeat += Clock.ElapsedFrameTime;

            // Beat on the interval, or right away if what we'd report just changed.
            bool changed = state != lastState || map != lastMap;
            if (!changed && sinceBeat < interval_ms)
                return;

            sinceBeat = 0;
            lastState = state;
            lastMap = map;
            auth.ReportPresence(state, map);
        }
    }
}
