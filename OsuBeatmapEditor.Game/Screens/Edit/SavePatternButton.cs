using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A left-column button that quick-saves the current selection to the Pattern Gallery. It only appears
    /// while <b>Shift is held over a non-empty selection</b> (the requested trigger), collapsing to nothing
    /// otherwise so it doesn't take up space in the tool column.
    /// </summary>
    public partial class SavePatternButton : ClickableContainer, IHasTooltip
    {
        /// <summary>Whether a saveable selection exists (beyond Shift being held).</summary>
        public Func<bool>? ShouldShow;

        /// <summary>Invoked on click to capture + save the selection.</summary>
        public Action? OnSave;

        private Box background = null!;
        private bool shown = true; // force the first Update to settle to the hidden state

        public SavePatternButton()
        {
            Width = 124;
            Height = 0;
            Alpha = 0;
            // Stay "present" while invisible so Update() keeps running (it polls Shift + selection);
            // a non-present drawable has its Update() skipped, which would leave the button stuck hidden.
            AlwaysPresent = true;
            Masking = true;
            CornerRadius = EditorTheme.Radius.Md;
            Action = () => OnSave?.Invoke();
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Selection },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Save pattern",
                    Colour = EditorTheme.Colours.Sunken,
                    Font = EditorTheme.Type.BodyStrong(),
                },
            };
        }

        public LocalisableString TooltipText => "Save the selection to your Pattern Gallery (Shift + selection)";

        protected override void Update()
        {
            base.Update();

            bool shift = GetContainingInputManager()?.CurrentState.Keyboard.ShiftPressed ?? false;
            bool wantShown = shift && (ShouldShow?.Invoke() ?? false);
            if (wantShown == shown)
                return;

            shown = wantShown;
            this.FadeTo(wantShown ? 1 : 0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            this.ResizeHeightTo(wantShown ? EditorTheme.Sizing.RowHeight : 0, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Accent, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(EditorTheme.Colours.Selection, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            base.OnHoverLost(e);
        }
    }
}
