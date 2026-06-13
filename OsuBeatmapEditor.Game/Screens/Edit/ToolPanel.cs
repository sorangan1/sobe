using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>The editor's composing tools (mirrors osu!lazer's left-hand toolbox).</summary>
    public enum EditorTool
    {
        Selection,
        Circle,
        Slider,
        Spinner,
    }

    /// <summary>
    /// A vertical toolbox on the left of the editor that shows - and lets the user pick - the active
    /// composing tool, highlighting the current one. Spinner is shown for completeness but not yet placeable.
    /// </summary>
    public partial class ToolPanel : CompositeDrawable
    {
        /// <summary>Raised when a tool row is clicked.</summary>
        public Action<EditorTool>? ToolSelected;

        private readonly Dictionary<EditorTool, ToolRow> rows = new Dictionary<EditorTool, ToolRow>();

        private static readonly (EditorTool tool, int key, string label)[] entries =
        {
            (EditorTool.Selection, 1, "Select"),
            (EditorTool.Circle, 2, "Circle"),
            (EditorTool.Slider, 3, "Slider"),
            (EditorTool.Spinner, 4, "Spinner"),  // placeable: click to start, scrub forward, click to end
        };

        public ToolPanel()
        {
            AutoSizeAxes = Axes.Both;

            var flow = new FillFlowContainer
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, 6),
            };

            foreach (var (tool, key, label) in entries)
            {
                var row = new ToolRow(key, label, disabled: false, () => ToolSelected?.Invoke(tool));
                rows[tool] = row;
                flow.Add(row);
            }

            InternalChild = flow;
        }

        /// <summary>Highlights the row for the active tool.</summary>
        public void SetActive(EditorTool tool)
        {
            foreach (var (key, row) in rows)
                row.SetActive(key == tool);
        }

        private partial class ToolRow : ClickableContainer
        {
            private readonly bool disabled;
            private Box background = null!;
            private SpriteText label = null!;
            private readonly string text;
            private readonly int key;

            public ToolRow(int key, string label, bool disabled, Action onClick)
            {
                this.key = key;
                text = label;
                this.disabled = disabled;
                Action = onClick;

                Size = new Vector2(124, EditorTheme.Sizing.RowHeight);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
                Alpha = disabled ? 0.55f : 1f;

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
                    this.label = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 32,
                        Text = text,
                        Colour = disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            public void SetActive(bool active)
            {
                // Active tool = the sanctioned solid-accent case (a persistent selected state, dark-on-accent).
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                label.FadeColour(active ? EditorTheme.Colours.Sunken : (disabled ? EditorTheme.Colours.TextFaint : EditorTheme.Colours.Text), EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }
        }
    }
}
