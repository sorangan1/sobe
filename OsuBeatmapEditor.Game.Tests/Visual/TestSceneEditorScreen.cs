using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Screens.Edit;
using NUnit.Framework;

namespace OsuBeatmapEditor.Game.Tests.Visual
{
    [TestFixture]
    public partial class TestSceneEditorScreen : OsuBeatmapEditorTestScene
    {
        public TestSceneEditorScreen()
        {
            var stack = new ScreenStack { RelativeSizeAxes = Axes.Both };
            Add(stack);

            // Load the first real osu!lazer beatmap that has a decodable difficulty, if any.
            var set = BeatmapStore.LoadAll()
                                  .FirstOrDefault(s => s.Difficulties.Any(d => d.OsuFileHash.Length > 0));

            if (set != null)
            {
                var difficulty = set.Difficulties.First(d => d.OsuFileHash.Length > 0);
                stack.Push(new EditorScreen(set, difficulty));
            }
        }
    }
}
