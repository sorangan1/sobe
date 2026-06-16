using OsuBeatmapEditor.Game.Graphics;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// One beatmap-discussion ("mod") entry as served by the sobe backend (which proxies osu! API v2's
    /// <c>GET /beatmapsets/discussions</c>). Used by the editor's Modding Mode to draw timeline bubbles and
    /// the message list. <see cref="TimestampMs"/> is the in-song time the mod points at (null = general note).
    /// </summary>
    public class ModdingDiscussion
    {
        public long Id { get; set; }

        /// <summary>The diff this mod targets, or null for a set-wide / general discussion.</summary>
        public long? BeatmapId { get; set; }

        public long UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        /// <summary>osu! discussion type: problem / suggestion / praise / mapper_note / hype / review.</summary>
        public string MessageType { get; set; } = string.Empty;

        /// <summary>In-song timestamp in milliseconds, or null for an untimed (general) discussion.</summary>
        public int? TimestampMs { get; set; }

        public bool Resolved { get; set; }

        public string Message { get; set; } = string.Empty;

        public System.DateTimeOffset? CreatedAt { get; set; }

        /// <summary>Colour a mod is drawn in, keyed off its osu! discussion type (chosen by the user).</summary>
        public static Color4 TypeColour(string messageType) => messageType switch
        {
            "problem" => EditorTheme.Colours.Error,      // red
            "suggestion" => EditorTheme.Colours.Selection, // yellow
            "praise" => EditorTheme.Colours.Velocity,    // green
            "hype" => EditorTheme.Colours.Accent,        // pink
            "review" => EditorTheme.Colours.Info,        // blue
            _ => EditorTheme.Colours.TextMuted,          // mapper_note / unknown -> grey
        };

        /// <summary>Short human label for a discussion type, for chips/tooltips.</summary>
        public static string TypeLabel(string messageType) => messageType switch
        {
            "problem" => "Problem",
            "suggestion" => "Suggestion",
            "praise" => "Praise",
            "mapper_note" => "Note",
            "hype" => "Hype",
            "review" => "Review",
            _ => messageType,
        };
    }
}
