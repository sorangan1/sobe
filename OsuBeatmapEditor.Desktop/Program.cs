using osu.Framework.Platform;
using osu.Framework;
using OsuBeatmapEditor.Game;

namespace OsuBeatmapEditor.Desktop
{
    public static class Program
    {
        public static void Main()
        {
            using (GameHost host = Host.GetSuitableDesktopHost(@"OsuBeatmapEditor"))
            using (osu.Framework.Game game = new OsuBeatmapEditorGame())
                host.Run(game);
        }
    }
}
