namespace OsuBeatmapEditor.Game
{
    /// <summary>
    /// Single source of truth for the app's identity (name + version). Referenced by the window title and
    /// the beta-notice popup so they never drift apart.
    /// </summary>
    public static class AppInfo
    {
        /// <summary>Short brand name shown in the window title bar.</summary>
        public const string Name = "sobe";

        /// <summary>Full name: "sobe" = sorangan osu beatmap editor.</summary>
        public const string FullName = "sobe (sorangan osu beatmap editor)";

        /// <summary>
        /// Current build version. The editor is pre-1.0 and feature-rich but unstable, hence the beta tag.
        /// </summary>
        public const string Version = "0.9.61-beta";
    }
}
