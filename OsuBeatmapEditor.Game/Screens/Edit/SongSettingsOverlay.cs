using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Per-beatmap settings: "General" (metadata) and "Difficulty" (HP/CS/AR/OD), mirroring the
    /// equivalent panels in osu!lazer's editor. Edits flow into the cached <see cref="EditableBeatmap"/>.
    /// </summary>
    public partial class SongSettingsOverlay : TabbedOverlay
    {
        [Resolved]
        private EditableBeatmap beatmap { get; set; } = null!;

        private readonly Bindable<string> lastSection = new Bindable<string>();

        protected override string Heading => "Song Setup";
        protected override Bindable<string>? LastSectionStore => lastSection;

        protected override (string name, Func<Drawable> content)[] CreateSections() => new (string, Func<Drawable>)[]
        {
            ("General", buildGeneralSection),
            ("Difficulty", buildDifficultySection),
            ("Colours", buildColoursSection),
        };

        private Drawable buildColoursSection() => flow(
            note("The beatmap's own combo colours (saved to the map). The editor renders these when "
                 + "\"Use beatmap colours\" is on in Settings; otherwise the editor palette is used."),
            new MapColoursEditor());

        private static Drawable note(string text)
        {
            var flow = new TextFlowContainer(t =>
            {
                t.Colour = EditorTheme.Colours.TextMuted;
                t.Font = EditorTheme.Type.Body();
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };
            flow.AddText(text);
            return flow;
        }

        private Drawable buildGeneralSection() => flow(
            field("Artist", beatmap.ArtistUnicode),
            field("Romanised artist", beatmap.Artist),
            field("Title", beatmap.TitleUnicode),
            field("Romanised title", beatmap.Title),
            field("Beatmap creator", beatmap.Creator),
            field("Difficulty name", beatmap.Version),
            field("Source", beatmap.Source),
            field("Tags", beatmap.Tags));

        private Drawable buildDifficultySection() => flow(
            diffRow("HP", beatmap.Hp),
            diffRow("CS", beatmap.Cs),
            diffRow("AR", beatmap.Ar),
            diffRow("OD", beatmap.Od),
            diffRow("Stack Leniency", beatmap.StackLeniency),
            diffRow("Slider Velocity", beatmap.SliderMultiplier),
            diffRow("Slider Tick Rate", beatmap.SliderTickRate));

        private Drawable field(string label, Bindable<string> bindable) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = label,
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Label(),
                },
                new EditorTextBox(bindable) { RelativeSizeAxes = Axes.X, Height = EditorTheme.Sizing.InputHeight },
            },
        };

        private Drawable diffRow(string label, BindableFloat bindable)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.RowHeight,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                    // Type the value directly; two-way bound to the slider below.
                    new NumberBox(bindable)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Width = 52,
                        Height = EditorTheme.Sizing.InputHeight,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 120, Right = 64 },
                        Child = new BasicSliderBar<float>
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = 8,
                            Current = bindable,
                            BackgroundColour = EditorTheme.Colours.Sunken,
                            SelectionColour = EditorTheme.Colours.Accent,
                        },
                    },
                },
            };
        }

        private static Drawable flow(params Drawable[] rows) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
            Children = rows,
        };
    }
}
