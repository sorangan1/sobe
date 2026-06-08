using System.Drawing;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Screens;
using OsuBeatmapEditor.Game.Screens.SongSelect;
using OsuBeatmapEditor.Game.UI;

namespace OsuBeatmapEditor.Game
{
    public partial class OsuBeatmapEditorGame : OsuBeatmapEditorGameBase
    {
        private ScreenStack screenStack = null!;

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            if (host.Window != null)
            {
                host.Window.Title = "osu! Beatmap Editor";
                // Enforce a usable minimum editor size; the window may be larger.
                host.Window.MinSize = new Size(1280, 720);
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var volume = new VolumeControl();

            Children = new Drawable[]
            {
                // Behind the screens: catches scroll over empty space to drive global volume.
                new ScrollCatcher { Scrolled = volume.AdjustMaster },
                screenStack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                // In front of everything so the popup is always visible.
                volume,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            screenStack.Push(new SongSelectScreen());
        }
    }
}
