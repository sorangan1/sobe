namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// A logged-in user as returned by the sobe backend (<c>/api/me</c>). Mirrors the server's user model;
    /// the osu! id doubles as the primary key there.
    /// </summary>
    public class SobeUser
    {
        public long Id { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? AvatarUrl { get; set; }

        public string? CountryCode { get; set; }

        /// <summary>Total active mapping time recorded server-side, in seconds.</summary>
        public long TotalMappingSeconds { get; set; }
    }
}
