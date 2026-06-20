namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// One beatmapset from the backend's <c>/api/beatmaps/search</c> proxy of osu! API v2. A flat, trimmed shape:
    /// enough to render a result row and build the website download link.
    /// </summary>
    public class BeatmapSetSearchResult
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;

        /// <summary>osu! ranked status string (e.g. "ranked", "loved", "graveyard").</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Card cover image URL, or null if the set has none.</summary>
        public string? Cover { get; set; }

        /// <summary>The osu! website link that downloads this set's <c>.osz</c> (requires a logged-in browser).</summary>
        public string DownloadUrl => $"https://osu.ppy.sh/beatmapsets/{Id}/download";
    }
}
