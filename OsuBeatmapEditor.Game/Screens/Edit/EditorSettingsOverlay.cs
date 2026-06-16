using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using OsuBeatmapEditor.Game.Updates;
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

        [Resolved(CanBeNull = true)]
        private UpdateManager? updates { get; set; }

        protected override string Heading => "Settings";
        protected override Bindable<string>? LastSectionStore => settings.LastSection;

        protected override (string name, Func<Drawable> content)[] CreateSections() => new (string, Func<Drawable>)[]
        {
            ("Color", buildColourSection),
            ("Objects", buildObjectsSection),
            ("Timeline", buildTimelineSection),
            ("Audio", buildAudioSection),
            ("Updates", buildUpdatesSection),
            ("Shortcuts", buildShortcutsSection),
        };

        private Drawable buildUpdatesSection()
        {
            var status = new SpriteText { Colour = EditorTheme.Colours.TextMuted, Font = EditorTheme.Type.Body() };
            var button = new OsuButton("Check for updates", OsuColour.Surface) { Size = new Vector2(190, 36) };

            void refresh()
            {
                switch (updates?.State.Value)
                {
                    case Updates.UpdateState.Checking:
                        status.Text = "Checking for updates...";
                        button.Text = "Check for updates";
                        break;

                    case Updates.UpdateState.UpToDate:
                        status.Text = "You're on the latest version.";
                        button.Text = "Check again";
                        break;

                    case Updates.UpdateState.UpdateAvailable:
                        status.Text = $"Version {updates.LatestVersion.Value} is available.";
                        button.Text = updates.CanSelfInstall ? "Install from main menu" : "Open download page";
                        break;

                    case Updates.UpdateState.Downloading:
                        status.Text = $"Downloading {updates.LatestVersion.Value}... {updates.Progress.Value * 100:0}%";
                        button.Text = "Downloading...";
                        break;

                    case Updates.UpdateState.ReadyToRestart:
                        status.Text = $"Version {updates.LatestVersion.Value} is ready - restart from the main menu.";
                        button.Text = "Ready";
                        break;

                    case Updates.UpdateState.Failed:
                        status.Text = "Couldn't check for updates.";
                        button.Text = "Try again";
                        break;

                    default:
                        status.Text = "Up to date checks run automatically on launch.";
                        button.Text = "Check for updates";
                        break;
                }
            }

            button.Action = () =>
            {
                if (updates == null)
                    return;

                if (updates.State.Value == Updates.UpdateState.UpdateAvailable && !updates.CanSelfInstall)
                    updates.OpenReleasesPage();
                else
                    updates.CheckForUpdatesOnce();

                refresh();
            };

            updates?.State.BindValueChanged(_ => refresh());
            updates?.Progress.BindValueChanged(_ => refresh());
            refresh();

            return section(
                toggleRow("Automatic updates", settings.AutoUpdate),
                new SpriteText
                {
                    Text = $"Current version: {AppInfo.Version}",
                    Colour = EditorTheme.Colours.TextFaint,
                    Font = EditorTheme.Type.Label(),
                },
                status,
                button);
        }

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
            controlWithReset(new NumberBox(value) { Width = 56, Height = EditorTheme.Sizing.InputHeight }, value));

        private Drawable buildColourSection() => section(
            toggleRow("Use beatmap (skin) colours", settings.UseMapColours),
            colourRow("Timing point (BPM)", settings.UninheritedColour),
            colourRow("Timing point (inherited)", settings.InheritedColour),
            colourRow("Bookmark", settings.BookmarkColour),
            colourRow("Preview point", settings.PreviewPointColour),
            colourRow("Kiai", settings.KiaiColour));

        private Drawable toggleRow(string label, osu.Framework.Bindables.BindableBool value) =>
            SettingsLayout.LabeledRow(label, controlWithReset(new ToggleSwitch(value), value));

        private Drawable buildShortcutsSection() => section(
            new SpriteText
            {
                Text = "Default beatmap creator",
                Colour = EditorTheme.Colours.TextMuted,
                Font = EditorTheme.Type.Label(),
            },
            new EditorTextBox(settings.DefaultCreator) { RelativeSizeAxes = Axes.X, Height = EditorTheme.Sizing.InputHeight },
            new Container { RelativeSizeAxes = Axes.X, Height = EditorTheme.Spacing.Md },
            toggleRow("Show beta notice on open", settings.ShowBetaPopup),
            new Container { RelativeSizeAxes = Axes.X, Height = EditorTheme.Spacing.Md },
            shortcutRow("Play / Pause", settings.PlayPauseKey),
            shortcutRow("Exit editor", settings.ExitKey),
            shortcutRow("Song setup", settings.SongSetupKey),
            shortcutRow("Settings", settings.SettingsKey),
            shortcutRow("Timing points", settings.TimingPointsKey),
            shortcutRow("Hitsound lanes", settings.HitsoundsKey),
            shortcutRow("Distance snap toggle", settings.DistanceSnapKey),
            shortcutRow("Convert slider to stream", settings.ConvertStreamKey),
            shortcutRow("Modding mode", settings.ModdingModeKey));

        private Drawable shortcutRow(string label, Bindable<Shortcut> shortcut) =>
            SettingsLayout.LabeledRow(label, controlWithReset(new KeyRebindButton(shortcut), shortcut));

        private Drawable colourRow(string label, Bindable<Colour4> colour) =>
            SettingsLayout.LabeledRow(label, controlWithReset(new ColourSwatch(colour), colour));

        /// <summary>
        /// A control paired with a <see cref="BindableResetButton{T}"/> (to its right) that only appears once
        /// the value has been changed from its default. Used as the right-hand control of a settings row.
        /// </summary>
        private static Drawable controlWithReset<T>(Drawable control, Bindable<T> bindable)
        {
            control.Anchor = Anchor.CentreLeft;
            control.Origin = Anchor.CentreLeft;

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    control,
                    new BindableResetButton<T>(bindable) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft },
                },
            };
        }

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
