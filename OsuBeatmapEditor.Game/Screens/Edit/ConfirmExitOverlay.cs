using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>Prompt shown when exiting with unsaved changes.</summary>
    public partial class ConfirmExitOverlay : VisibilityContainer
    {
        public Action? OnSave;
        public Action? OnDiscard;

        private Container panel = null!;

        protected override bool StartHidden => true;

        public ConfirmExitOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.6f },
                panel = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(460, 200),
                    Masking = true,
                    CornerRadius = 12,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundRaised },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Margin = new MarginPadding { Top = 36 },
                            Text = "You have unsaved changes.",
                            Colour = OsuColour.Text,
                            Font = FontUsage.Default.With(size: 22, weight: "SemiBold"),
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomCentre,
                            Origin = Anchor.BottomCentre,
                            Margin = new MarginPadding { Bottom = 28 },
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(12, 0),
                            Children = new Drawable[]
                            {
                                new OsuButton("Save", OsuColour.Pink) { Size = new Vector2(120, 48), Action = () => { Hide(); OnSave?.Invoke(); } },
                                new OsuButton("Don't save", OsuColour.Surface) { Size = new Vector2(130, 48), Action = () => { Hide(); OnDiscard?.Invoke(); } },
                                new OsuButton("Cancel", OsuColour.Surface) { Size = new Vector2(110, 48), Action = () => Hide() },
                            },
                        },
                    },
                },
            };
        }

        protected override void PopIn()
        {
            this.FadeIn(150, Easing.OutQuint);
            panel.ScaleTo(1, 300, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(150, Easing.OutQuint);
            panel.ScaleTo(0.97f, 150, Easing.OutQuint);
        }

        protected override bool OnClick(ClickEvent e)
        {
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }
    }
}
