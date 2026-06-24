using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
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
                    Width = 468,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    BorderThickness = 1f,
                    BorderColour = EditorTheme.Colours.Border,
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = Color4.Black.Opacity(0.45f),
                        Radius = 22,
                        Offset = new Vector2(0, 6),
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding { Horizontal = 32, Top = 30, Bottom = 26 },
                            Spacing = new Vector2(0, 14),
                            Children = new Drawable[]
                            {
                                // Amber warning badge - reads as "heads up" without the old plain-text look.
                                new CircularContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Size = new Vector2(52),
                                    Masking = true,
                                    Children = new Drawable[]
                                    {
                                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Warning.Opacity(0.16f) },
                                        new SpriteIcon
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Icon = FontAwesome.Solid.ExclamationTriangle,
                                            Size = new Vector2(24),
                                            Colour = EditorTheme.Colours.Warning,
                                        },
                                    },
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Text = "Unsaved changes",
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.Title(),
                                },
                                new TextFlowContainer(t => t.Font = EditorTheme.Type.Body())
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    TextAnchor = Anchor.TopCentre,
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Text = "If you exit now, your latest edits to this difficulty will be lost.",
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Margin = new MarginPadding { Top = 6 },
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(10, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Save & exit", EditorTheme.Colours.Accent)
                                        {
                                            Icon = FontAwesome.Solid.Save,
                                            Size = new Vector2(148, 46),
                                            Action = () => { Hide(); OnSave?.Invoke(); },
                                        },
                                        new OsuButton("Don't save", EditorTheme.Colours.Error)
                                        {
                                            Icon = FontAwesome.Solid.TrashAlt,
                                            Size = new Vector2(140, 46),
                                            Action = () => { Hide(); OnDiscard?.Invoke(); },
                                        },
                                        new OsuButton("Cancel", OsuColour.Surface)
                                        {
                                            Icon = FontAwesome.Solid.Times,
                                            Size = new Vector2(118, 46),
                                            Action = () => Hide(),
                                        },
                                    },
                                },
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
