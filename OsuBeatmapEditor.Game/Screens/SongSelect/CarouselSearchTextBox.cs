using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// The carousel's search box. It holds focus so anything the user types filters immediately
    /// (osu!lazer-style), while letting navigation/global keys bubble up to the screen and clearing
    /// the query on Escape. Styled to the editor design system.
    /// </summary>
    public partial class CarouselSearchTextBox : BasicTextBox
    {
        public CarouselSearchTextBox()
        {
            // Don't drop focus when the user presses Enter (which opens the selected map).
            ReleaseFocusOnCommit = false;

            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
            BackgroundUnfocused = EditorTheme.Colours.Surface;
            BackgroundFocused = EditorTheme.Colours.Control;
            BackgroundCommit = EditorTheme.Colours.Accent;
        }

        // Always wants focus, so typing anywhere lands in the search box.
        public override bool RequestsFocus => true;

        protected override void Update()
        {
            base.Update();

            // Robustly hold focus: whenever nothing else is focused (e.g. after a click on a map card or
            // empty space), grab it back so the user can always type to search - like osu!lazer. We only
            // claim it when free, so dropdowns / other inputs keep focus while they're active.
            var input = GetContainingInputManager();
            if (input != null && input.FocusedDrawable == null)
                input.ChangeFocus(this);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                // Escape clears the whole query (and is swallowed); if already empty, let it bubble.
                case Key.Escape:
                    if (Text.Length == 0)
                        return false;

                    Text = string.Empty;
                    return true;

                // Carousel navigation and global shortcuts must reach the screen, not the text box.
                case Key.Up:
                case Key.Down:
                case Key.Left:
                case Key.Right:
                case Key.Enter:
                case Key.KeypadEnter:
                case Key.F2:
                case Key.F5:
                    return false;

                // Ctrl+Space toggles the preview - don't type a space.
                case Key.Space when e.ControlPressed:
                    return false;
            }

            return base.OnKeyDown(e);
        }

        // A focused TextBox moves its caret on Left/Right via PlatformAction key bindings (a separate pipeline
        // from OnKeyDown), which would otherwise swallow them. Decline those so Left/Right reach the screen and
        // step set-by-set through the carousel - the box is single-line, so caret arrows aren't needed here.
        public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
        {
            switch (e.Action)
            {
                case PlatformAction.MoveBackwardChar:
                case PlatformAction.MoveForwardChar:
                    return false;
            }

            return base.OnPressed(e);
        }
    }
}
