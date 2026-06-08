using osu.Framework.iOS;
using OsuBeatmapEditor.Game;

namespace OsuBeatmapEditor.iOS
{
    /// <inheritdoc />
    public class AppDelegate : GameApplicationDelegate
    {
        /// <inheritdoc />
        protected override osu.Framework.Game CreateGame() => new OsuBeatmapEditorGame();
    }
}
