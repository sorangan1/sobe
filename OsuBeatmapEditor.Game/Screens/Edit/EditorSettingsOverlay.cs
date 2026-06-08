using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Editor settings dialog with "Color" and "Shortcuts" sections. Reopens to the last-used section.
    /// </summary>
    public partial class EditorSettingsOverlay : TabbedOverlay
    {
        [Resolved]
        private EditorSettings settings { get; set; } = null!;

        protected override string Heading => "Settings";
        protected override Bindable<string>? LastSectionStore => settings.LastSection;

        protected override (string name, Func<Drawable> content)[] CreateSections() => new (string, Func<Drawable>)[]
        {
            ("Color", buildColourSection),
            ("Timeline", buildTimelineSection),
            ("Shortcuts", buildShortcutsSection),
        };

        private Drawable buildTimelineSection() => section(
            colourRow("Measure line", settings.MeasureLineColour),
            heightRow("Measure line height", settings.MeasureLineHeight),
            colourRow("Beat line", settings.BeatLineColour),
            heightRow("Beat line height", settings.BeatLineHeight),
            colourRow("Half-beat line (1/2)", settings.HalfBeatLineColour),
            colourRow("Quarter-beat line (1/4)", settings.QuarterBeatLineColour),
            heightRow("Quarter line height", settings.QuarterLineHeight));

        private Drawable heightRow(string label, BindableFloat value) => SettingsLayout.LabeledRow(label,
            new NumberBox(value) { Width = 56, Height = 30, Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight });

        private Drawable buildColourSection() => section(
            colourRow("Timing point (BPM)", settings.UninheritedColour),
            colourRow("Timing point (inherited)", settings.InheritedColour),
            colourRow("Bookmark", settings.BookmarkColour),
            colourRow("Preview point", settings.PreviewPointColour),
            colourRow("Kiai", settings.KiaiColour),
            colourRow("Editor background", settings.EditorBackgroundColour));

        private Drawable buildShortcutsSection() => section(
            new SpriteText
            {
                Text = "Default beatmap creator",
                Colour = OsuColour.TextMuted,
                Font = FontUsage.Default.With(size: 14),
            },
            new EditorTextBox(settings.DefaultCreator) { RelativeSizeAxes = Axes.X, Height = 34 },
            new Container { RelativeSizeAxes = Axes.X, Height = 6 },
            SettingsLayout.LabeledRow("Play / Pause", new KeyRebindButton(settings.PlayPauseKey)),
            SettingsLayout.LabeledRow("Exit editor", new KeyRebindButton(settings.ExitKey)));

        private Drawable colourRow(string label, Bindable<Colour4> colour) => SettingsLayout.LabeledRow(label,
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    new ColourSwatch(colour) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                    new ResetButton(colour.SetDefault) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                },
            });

        private static Drawable section(params Drawable[] rows) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children = rows,
        };
    }
}
