using osu.Framework.Platform;
using osu.Framework;

namespace OsuBeatmapEditor.Desktop
{
    public static class Program
    {
        public static void Main()
        {
            using (GameHost host = Host.GetSuitableDesktopHost(@"OsuBeatmapEditor"))
            using (osu.Framework.Game game = new OsuBeatmapEditorGameDesktop())
                host.Run(game);
        }
    }
}
