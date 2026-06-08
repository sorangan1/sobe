using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuBeatmapEditor.Game.Screens.SongSelect;
using NUnit.Framework;

namespace OsuBeatmapEditor.Game.Tests.Visual
{
    [TestFixture]
    public partial class TestSceneSongSelectScreen : OsuBeatmapEditorTestScene
    {
        public TestSceneSongSelectScreen()
        {
            Add(new ScreenStack(new SongSelectScreen()) { RelativeSizeAxes = Axes.Both });
        }
    }
}
