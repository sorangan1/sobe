using osu.Framework.Testing;

namespace OsuBeatmapEditor.Game.Tests.Visual
{
    public abstract partial class OsuBeatmapEditorTestScene : TestScene
    {
        protected override ITestSceneTestRunner CreateRunner() => new OsuBeatmapEditorTestSceneTestRunner();

        private partial class OsuBeatmapEditorTestSceneTestRunner : OsuBeatmapEditorGameBase, ITestSceneTestRunner
        {
            private TestSceneTestRunner.TestRunner runner;

            protected override void LoadAsyncComplete()
            {
                base.LoadAsyncComplete();
                Add(runner = new TestSceneTestRunner.TestRunner());
            }

            public void RunTestBlocking(TestScene test) => runner.RunTestBlocking(test);
        }
    }
}
