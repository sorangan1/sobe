using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>The tools available while in Review mode.</summary>
    public enum ReviewTool
    {
        Select,
        Note,
        Draw,
    }

    /// <summary>
    /// The Review-mode tool box on the left of the editor (mirrors <see cref="ToolPanel"/>): pick Select / Note /
    /// Line, highlighting the active one. Shown only while Review mode is on (in place of the composing tools).
    /// </summary>
    public partial class ReviewToolPanel : CompositeDrawable
    {
        public Action<ReviewTool>? ToolSelected;

        private readonly Dictionary<ReviewTool, ToolRow> rows = new Dictionary<ReviewTool, ToolRow>();

        private static readonly (ReviewTool tool, int key, string label, IconUsage icon)[] entries =
        {
            (ReviewTool.Select, 1, "Select", FontAwesome.Solid.MousePointer),
            (ReviewTool.Note, 2, "Note", FontAwesome.Regular.StickyNote),
            (ReviewTool.Draw, 3, "Draw", FontAwesome.Solid.PenNib),
        };

        public ReviewToolPanel()
        {
            AutoSizeAxes = Axes.Both;

            var flow = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, 6),
            };

            foreach (var (tool, key, label, icon) in entries)
            {
                var row = new ToolRow(key, label, icon, () => ToolSelected?.Invoke(tool));
                rows[tool] = row;
                flow.Add(row);
            }

            InternalChild = flow;
        }

        /// <summary>Highlights the row for the active tool.</summary>
        public void SetActive(ReviewTool tool)
        {
            foreach (var (key, row) in rows)
                row.SetActive(key == tool);
        }

        private partial class ToolRow : ClickableContainer
        {
            private Box background = null!;
            private SpriteText label = null!;
            private SpriteIcon iconSprite = null!;
            private readonly string text;
            private readonly int key;
            private readonly IconUsage icon;
            private bool active;

            public ToolRow(int key, string label, IconUsage icon, Action onClick)
            {
                this.key = key;
                text = label;
                this.icon = icon;
                Action = onClick;

                Size = new Vector2(124, EditorTheme.Sizing.RowHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;

                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = EditorTheme.Spacing.Lg,
                        Text = key.ToString(),
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Label(numeric: true),
                    },
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 32,
                        Size = new Vector2(13),
                        Icon = icon,
                        Colour = EditorTheme.Colours.TextMuted,
                    },
                    this.label = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 54,
                        Text = text,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            public void SetActive(bool value)
            {
                active = value;
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                iconSprite.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.TextMuted, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!active)
                {
                    background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                    iconSprite.MoveToX(36, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    label.MoveToX(58, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                }
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                    background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                iconSprite.MoveToX(32, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.MoveToX(54, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }
        }
    }
}
