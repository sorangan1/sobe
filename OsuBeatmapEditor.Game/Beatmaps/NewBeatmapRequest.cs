using System.IO;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// User-supplied data for creating a brand new (empty) beatmap.
    /// </summary>
    public class NewBeatmapRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Creator { get; set; } = string.Empty;
        public string DifficultyName { get; set; } = "Normal";
        public double Bpm { get; set; } = 120;

        /// <summary>Absolute path to the source audio file chosen by the user.</summary>
        public string AudioPath { get; set; } = string.Empty;

        /// <summary>Whether all required fields are present and valid.</summary>
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Title)
            && !string.IsNullOrWhiteSpace(Artist)
            && Bpm > 0
            && !string.IsNullOrWhiteSpace(AudioPath)
            && File.Exists(AudioPath);
    }
}
