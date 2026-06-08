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
        };

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
            diffRow("Stack Leniency", beatmap.StackLeniency));

        private Drawable field(string label, Bindable<string> bindable) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = label,
                    Colour = OsuColour.TextMuted,
                    Font = FontUsage.Default.With(size: 14),
                },
                new EditorTextBox(bindable) { RelativeSizeAxes = Axes.X, Height = 32 },
            },
        };

        private Drawable diffRow(string label, BindableFloat bindable)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 40,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Colour = OsuColour.Text,
                        Font = FontUsage.Default.With(size: 16, weight: "SemiBold"),
                    },
                    // Type the value directly; two-way bound to the slider below.
                    new NumberBox(bindable)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Width = 52,
                        Height = 30,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 50, Right = 64 },
                        Child = new BasicSliderBar<float>
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = 16,
                            Current = bindable,
                            BackgroundColour = OsuColour.Surface,
                            SelectionColour = OsuColour.Pink,
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
            Spacing = new Vector2(0, 12),
            Children = rows,
        };
    }
}
