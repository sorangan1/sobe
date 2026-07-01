using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Skinning;
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

        [Resolved(CanBeNull = true)]
        private SkinManager? skinManager { get; set; }

        [Resolved(CanBeNull = true)]
        private GameHost? host { get; set; }

        protected override string Heading => "Settings";
        protected override Bindable<string>? LastSectionStore => settings.LastSection;

        protected override (string name, IconUsage icon, Func<Drawable> content)[] CreateSections() => new (string, IconUsage, Func<Drawable>)[]
        {
            ("General", FontAwesome.Solid.Cog, buildGeneralSection),
            ("Skins", FontAwesome.Solid.PaintBrush, buildSkinsSection),
            ("Appearance", FontAwesome.Solid.Palette, buildColourSection),
            ("Objects", FontAwesome.Solid.Bullseye, buildObjectsSection),
            ("Timeline", FontAwesome.Solid.RulerHorizontal, buildTimelineSection),
            ("Audio", FontAwesome.Solid.VolumeUp, buildAudioSection),
            ("Performance", FontAwesome.Solid.TachometerAlt, buildPerformanceSection),
            ("Shortcuts", FontAwesome.Solid.Keyboard, buildShortcutsSection),
            ("Updates", FontAwesome.Solid.SyncAlt, buildUpdatesSection),
        };

        private Drawable buildGeneralSection() => section(
            fieldLabel("Default beatmap creator"),
            new EditorTextBox(settings.DefaultCreator) { RelativeSizeAxes = Axes.X, Height = EditorTheme.Sizing.InputHeight },
            description("Auto-filled into the creator field of new and edited beatmaps."),
            divider(),
            toggleRow("Show beta notice on open", settings.ShowBetaPopup),
            description("Shows the welcome / beta disclaimer each time the editor opens."),
            divider(),
            toggleRow("Discord Rich Presence", settings.DiscordRichPresence),
            description("Shows your current activity (the map you're editing, or browsing) on your Discord profile. "
                       + "Requires the Discord desktop client to be running."));

        private const string skin_none_label = "(None)";

        private Drawable buildSkinsSection()
        {
            string[] installed = skinManager?.AvailableSkins() ?? Array.Empty<string>();

            var items = new List<string> { skin_none_label };
            items.AddRange(installed);

            var dropdown = new ThemedDropdown<string>
            {
                RelativeSizeAxes = Axes.X,
                Items = items,
            };

            // Reflect the persisted selection (falling back to None if it points at a since-removed skin), and
            // push changes back to the setting (the SkinManager rebuilds the active skin off SkinName).
            dropdown.Current.Value = installed.Contains(settings.SkinName.Value) ? settings.SkinName.Value : skin_none_label;
            dropdown.Current.BindValueChanged(e =>
                settings.SkinName.Value = e.NewValue == skin_none_label ? string.Empty : e.NewValue);

            var openFolder = new OsuButton("Open skins folder", OsuColour.Surface) { Size = new Vector2(190, 36) };
            openFolder.Action = () =>
            {
                if (skinManager != null)
                    host?.PresentFileExternally(skinManager.SkinsPath);
            };

            return section(
                fieldLabel("Active skin"),
                dropdown,
                description("Draws hit objects with your osu! skin's textures and plays its hitsounds. Anything the "
                           + "skin doesn't include falls back to the editor's built-in look."),
                divider(),
                description("To import a skin, drag a .osk file onto the window. Imported skins are unpacked into the "
                           + "skins folder below; changes there appear after reopening this list."),
                openFolder);
        }

        private Drawable buildPerformanceSection() => section(
            toggleRow("Power saving (cap to refresh rate)", settings.PowerSaving),
            description("Limits the frame rate to your monitor's refresh rate instead of running at double it. "
                       + "Lowers GPU/CPU use noticeably on high-refresh displays, with slightly more input latency. "
                       + "The app also throttles itself automatically while its window isn't focused."));

        private Drawable buildUpdatesSection()
        {
            // A wrapping text block so long status lines stay inside the panel instead of clipping at its edge.
            var status = new TextFlowContainer(t => { t.Colour = EditorTheme.Colours.TextMuted; t.Font = EditorTheme.Type.Body(); })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };
            var button = new OsuButton("Check for updates", OsuColour.Surface) { Size = new Vector2(190, 36) };

            void setStatus(string text)
            {
                status.Clear();
                status.AddText(text);
            }

            void refresh()
            {
                switch (updates?.State.Value)
                {
                    case Updates.UpdateState.Checking:
                        setStatus("Checking for updates...");
                        button.Text = "Check for updates";
                        break;

                    case Updates.UpdateState.UpToDate:
                        setStatus("You're on the latest version.");
                        button.Text = "Check again";
                        break;

                    case Updates.UpdateState.UpdateAvailable:
                        setStatus($"Version {updates.LatestVersion.Value} is available.");
                        button.Text = updates.CanSelfInstall ? "Install from main menu" : "Open download page";
                        break;

                    case Updates.UpdateState.Downloading:
                        setStatus($"Downloading {updates.LatestVersion.Value}... {updates.Progress.Value * 100:0}%");
                        button.Text = "Downloading...";
                        break;

                    case Updates.UpdateState.ReadyToRestart:
                        setStatus($"Version {updates.LatestVersion.Value} is ready - restart from the main menu.");
                        button.Text = "Ready";
                        break;

                    case Updates.UpdateState.Failed:
                        setStatus("Couldn't check for updates.");
                        button.Text = "Try again";
                        break;

                    default:
                        setStatus("Up to date checks run automatically on launch.");
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
            SettingsLayout.LabeledRow("Output device", new AudioDeviceSetting()),
            heightRow("Audio offset (ms)", settings.AudioOffset),
            description("Shifts the playhead during playback to match output latency (e.g. Bluetooth headphones). "
                       + "A negative value delays the visuals so what you see lines up with what you hear."),
            divider(),
            toggleRow("Ignore beatmap hitsounds", settings.IgnoreBeatmapHitsounds),
            description("Plays the default skin samples instead of the map's own custom hitsounds, so you hear "
                       + "the plain normal/whistle/finish/clap sounds - like osu!lazer's beatmap-hitsounds toggle."));

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
            shortcutRow("Play / Pause", settings.PlayPauseKey),
            shortcutRow("Exit editor", settings.ExitKey),
            shortcutRow("Song setup", settings.SongSetupKey),
            shortcutRow("Settings", settings.SettingsKey),
            shortcutRow("Timing points", settings.TimingPointsKey),
            shortcutRow("Hitsound lanes", settings.HitsoundsKey),
            shortcutRow("Distance snap toggle", settings.DistanceSnapKey),
            shortcutRow("Convert slider to stream", settings.ConvertStreamKey),
            shortcutRow("Modding mode", settings.ModdingModeKey),
            shortcutRow("Pattern gallery", settings.PatternGalleryKey),
            shortcutRow("Add BPM point", settings.AddBpmPointKey),
            shortcutRow("Add SV point", settings.AddSvPointKey));

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

        /// <summary>
        /// A muted helper paragraph below a control. Wraps to the panel width (it fills its X axis and grows
        /// down), so long explanations stay inside the panel instead of running off the edge and being clipped.
        /// </summary>
        private static Drawable description(string text)
        {
            var flow = new TextFlowContainer(t =>
            {
                t.Colour = EditorTheme.Colours.TextFaint;
                t.Font = EditorTheme.Type.Label();
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                // Pull up a touch so the note reads as attached to the control above it, not a new row.
                Margin = new MarginPadding { Top = -EditorTheme.Spacing.Xs },
            };
            flow.AddText(text);
            return flow;
        }

        /// <summary>A small caption above a full-width input (e.g. the creator text box).</summary>
        private static Drawable fieldLabel(string text) => new SpriteText
        {
            Text = text,
            Colour = EditorTheme.Colours.TextMuted,
            Font = EditorTheme.Type.Label(),
        };

        /// <summary>A hairline rule that separates groups of settings within one section.</summary>
        private static Drawable divider() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = EditorTheme.Spacing.Sm,
            Child = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.BorderThickness,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Colour = EditorTheme.Colours.Border,
            },
        };
    }
}
