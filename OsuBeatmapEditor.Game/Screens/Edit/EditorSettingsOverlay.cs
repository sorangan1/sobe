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
            ("Objects", buildObjectsSection),
            ("Timeline", buildTimelineSection),
            ("Audio", buildAudioSection),
            ("Shortcuts", buildShortcutsSection),
        };

        private Drawable buildAudioSection() => section(
            SettingsLayout.LabeledRow("Output device", new AudioDeviceSetting()));

        private Drawable buildObjectsSection() => section(
            colourRow("Combo colour 1", settings.ComboColour1),
            colourRow("Combo colour 2", settings.ComboColour2),
            colourRow("Combo colour 3", settings.ComboColour3),
            colourRow("Combo colour 4", settings.ComboColour4),
            heightRow("Object background opacity", settings.ObjectBackgroundOpacity),
            heightRow("Object outline thickness", settings.ObjectBorderThickness),
            heightRow("Slider tick size", settings.SliderTickSize),
            heightRow("Past object fade (ms)", settings.ObjectFadeOut));

        private Drawable buildTimelineSection() => section(
            colourRow("Measure line", settings.MeasureLineColour),
            heightRow("Measure line height", settings.MeasureLineHeight),
            colourRow("Beat line", settings.BeatLineColour),
            heightRow("Beat line height", settings.BeatLineHeight),
            colourRow("Half-beat line (1/2)", settings.HalfBeatLineColour),
            colourRow("Quarter-beat line (1/4)", settings.QuarterBeatLineColour),
            heightRow("Quarter line height", settings.QuarterLineHeight));

        private Drawable heightRow(string label, BindableFloat value) => SettingsLayout.LabeledRow(label,
            new NumberBox(value) { Width = 56, Height = EditorTheme.Sizing.InputHeight, Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight });

        private Drawable buildColourSection() => section(
            toggleRow("Use beatmap (skin) colours", settings.UseMapColours),
            colourRow("Timing point (BPM)", settings.UninheritedColour),
            colourRow("Timing point (inherited)", settings.InheritedColour),
            colourRow("Bookmark", settings.BookmarkColour),
            colourRow("Preview point", settings.PreviewPointColour),
            colourRow("Kiai", settings.KiaiColour),
            colourRow("Editor background", settings.EditorBackgroundColour));

        private Drawable toggleRow(string label, osu.Framework.Bindables.BindableBool value) =>
            SettingsLayout.LabeledRow(label, new ToggleSwitch(value) { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight });

        private Drawable buildShortcutsSection() => section(
            new SpriteText
            {
                Text = "Default beatmap creator",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Label(),
            },
            new EditorTextBox(settings.DefaultCreator) { RelativeSizeAxes = Axes.X, Height = EditorTheme.Sizing.InputHeight },
            new Container { RelativeSizeAxes = Axes.X, Height = EditorTheme.Spacing.Md },
            shortcutRow("Play / Pause", settings.PlayPauseKey),
            shortcutRow("Exit editor", settings.ExitKey),
            shortcutRow("Song setup", settings.SongSetupKey),
            shortcutRow("Settings", settings.SettingsKey),
            shortcutRow("Timing points", settings.TimingPointsKey),
            shortcutRow("Distance snap toggle", settings.DistanceSnapKey),
            shortcutRow("Convert slider to stream", settings.ConvertStreamKey));

        private Drawable shortcutRow(string label, Bindable<Shortcut> shortcut) => SettingsLayout.LabeledRow(label,
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    new KeyRebindButton(shortcut) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                    new ResetButton(shortcut.SetDefault) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                },
            });

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
            Spacing = new Vector2(0, EditorTheme.Spacing.Md),
            Children = rows,
        };
    }
}
