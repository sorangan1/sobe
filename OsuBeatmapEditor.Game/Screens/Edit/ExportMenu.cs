using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The little dropdown that opens under the editor's Export button: pick "Export as .osz" (the whole set) or
    /// "Export difficulty as .osu" (just the open difficulty). A full-screen catcher behind the panel closes it on
    /// any outside click. Positioned at the button via <see cref="ShowAt"/>.
    /// </summary>
    public partial class ExportMenu : VisibilityContainer
    {
        public Action? OnExportOsz;
        public Action? OnExportOsu;

        private Container panel = null!;

        protected override bool StartHidden => true;

        public ExportMenu()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [osu.Framework.Allocation.BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new ClickCatcher { Action = Hide },
                panel = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Sm,
                    BorderThickness = 1f,
                    BorderColour = EditorTheme.Colours.Border,
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = new Color4(0, 0, 0, 0.4f),
                        Radius = 6f,
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                        new FillFlowContainer
                        {
                            Direction = FillDirection.Vertical,
                            AutoSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new Row(FontAwesome.Solid.FileArchive, "Export as .osz", "Whole set (all difficulties + assets)",
                                    () => Fire(OnExportOsz)),
                                new Row(FontAwesome.Solid.FileAlt, "Export difficulty as .osu", "Just this difficulty's .osu",
                                    () => Fire(OnExportOsu)),
                            },
                        },
                    },
                },
            };
        }

        /// <summary>Opens the menu with its top-left corner at the given screen-space point (the export button's bottom-left).</summary>
        public void ShowAt(Vector2 screenSpacePoint)
        {
            // The overlay fills the screen, so its local space matches screen space; nudge down a couple px off the button.
            panel.Position = ToLocalSpace(screenSpacePoint) + new Vector2(0, 4);
            Show();
        }

        private void Fire(Action? action)
        {
            Hide();
            action?.Invoke();
        }

        protected override void PopIn() => this.FadeIn(80, Easing.OutQuint);
        protected override void PopOut() => this.FadeOut(80, Easing.OutQuint);

        /// <summary>One menu entry: an icon, a title and a small subtitle, highlighting on hover.</summary>
        private partial class Row : CompositeDrawable
        {
            private readonly Action onClick;
            private readonly Box background;

            public Row(IconUsage icon, string title, string subtitle, Action onClick)
            {
                this.onClick = onClick;

                AutoSizeAxes = Axes.Y;
                Width = 244;
                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Selection, Alpha = 0 },
                    new FillFlowContainer
                    {
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(10, 0),
                        Margin = new MarginPadding { Horizontal = 12, Vertical = 8 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Icon = icon,
                                Size = new Vector2(15),
                                Colour = EditorTheme.Colours.Text,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                            },
                            new FillFlowContainer
                            {
                                Direction = FillDirection.Vertical,
                                AutoSizeAxes = Axes.Both,
                                Spacing = new Vector2(0, 1),
                                Children = new Drawable[]
                                {
                                    new SpriteText { Text = title, Font = EditorTheme.Type.Label(), Colour = EditorTheme.Colours.Text },
                                    new SpriteText { Text = subtitle, Font = EditorTheme.Type.Caption(), Colour = EditorTheme.Colours.TextMuted },
                                },
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.Alpha = 0.35f;
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) => background.Alpha = 0f;

            // Consume the press so it doesn't reach the catcher behind the menu (which would close before the click lands).
            protected override bool OnMouseDown(MouseDownEvent e) => true;

            protected override bool OnClick(ClickEvent e)
            {
                onClick();
                return true;
            }
        }

        /// <summary>A transparent full-area layer behind the panel that closes the menu when clicked.</summary>
        private partial class ClickCatcher : Drawable
        {
            public Action? Action;

            public ClickCatcher()
            {
                RelativeSizeAxes = Axes.Both;
            }

            public override bool IsPresent => true;

            protected override bool OnClick(ClickEvent e)
            {
                Action?.Invoke();
                return true;
            }
        }
    }
}
