using OsuBeatmapEditor.Game;

namespace OsuBeatmapEditor.Desktop
{
    /// <summary>
    /// Desktop-specific game variant. Adds components that only make sense on desktop (Discord Rich Presence,
    /// which talks to the local Discord client over IPC) on top of the shared <see cref="OsuBeatmapEditorGame"/>.
    /// </summary>
    public partial class OsuBeatmapEditorGameDesktop : OsuBeatmapEditorGame
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Added at the game root so it resolves the cached StatisticsTracker / EditorSettings, like the
            // existing online PresenceReporter does.
            Add(new DiscordRichPresence());
        }
    }
}
