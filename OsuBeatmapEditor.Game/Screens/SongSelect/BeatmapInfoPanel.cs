using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Top-left readout for the selected map: title/artist, the difficulty and its mapper, length and BPM,
    /// object counts, and the difficulty stats (CS/AR/OD/HP). Sits on a translucent rounded panel so it stays
    /// legible over any background. Metadata updates instantly on selection; counts and stats are filled in
    /// after decoding the .osu off the update thread. Every numeric readout lives in a fixed-width chip so
    /// values never reflow the layout as they change.
    /// </summary>
    public partial class BeatmapInfoPanel : CompositeDrawable
    {
        private const float content_width = 540;
        private const float line_height = 22;
        private const float icon_size = 13;

        private SpriteText title = null!;
        private SpriteText artist = null!;
        private SpriteText difficultyName = null!;
        private SpriteText mapper = null!;
        private FillFlowContainer metaFlow = null!;
        private FillFlowContainer statsFlow = null!;

        // Guards against a stale async decode overwriting a newer selection.
        private int token;

        public BeatmapInfoPanel()
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = EditorTheme.Radius.Lg;
            BorderThickness = 1;
            BorderColour = EditorTheme.Colours.Border;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    BypassAutoSizeAxes = Axes.Both,
                    Colour = EditorTheme.Colours.Base,
                    Alpha = 0.88f,
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Y,
                    Width = content_width,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, EditorTheme.Spacing.Xxs),
                    Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Xl, Vertical = EditorTheme.Spacing.Lg },
                    Children = new Drawable[]
                    {
                        // Artist sits above the title as a small eyebrow, the way a track is actually credited.
                        artist = new SpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            Truncate = true,
                            Colour = EditorTheme.Colours.TextMuted,
                            Font = FontUsage.Default.With(size: 14),
                        },
                        title = new SpriteText
                        {
                            RelativeSizeAxes = Axes.X,
                            Truncate = true,
                            Colour = EditorTheme.Colours.Text,
                            Font = FontUsage.Default.With(size: 27, weight: "Bold"),
                        },
                        // Difficulty name + mapper.
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.X,
                            Height = line_height,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                            Margin = new MarginPadding { Top = EditorTheme.Spacing.Sm },
                            Children = new Drawable[]
                            {
                                difficultyName = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Colour = EditorTheme.Colours.Accent,
                                    Font = FontUsage.Default.With(size: 17, weight: "SemiBold"),
                                },
                                centred(new SpriteIcon { Icon = FontAwesome.Solid.User, Size = new Vector2(11), Colour = EditorTheme.Colours.TextFaint, Margin = new MarginPadding { Left = EditorTheme.Spacing.Sm } }),
                                mapper = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = FontUsage.Default.With(size: 14),
                                },
                            },
                        },
                        // Length / BPM / object counts on a single line, the way a real song-select strip reads.
                        metaFlow = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.X,
                            Height = line_height,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                            Margin = new MarginPadding { Top = EditorTheme.Spacing.Md },
                        },
                        // A hairline separates the descriptive metadata from the raw difficulty numbers.
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = EditorTheme.Colours.Border,
                            Margin = new MarginPadding { Vertical = EditorTheme.Spacing.Md },
                        },
                        statsFlow = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.X,
                            Height = line_height,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Xl, 0),
                        },
                    },
                },
            };
        }

        /// <summary>Shows the given map; decodes the .osu asynchronously to fill in counts and stats.</summary>
        public void SetMap(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            int mine = ++token;

            title.Text = set.Title;
            artist.Text = set.Artist;
            difficultyName.Text = diff.DifficultyName;
            mapper.Text = string.IsNullOrEmpty(set.Author) ? "unknown" : set.Author;
            metaFlow.Clear();
            statsFlow.Clear();

            string? path = LazerFileStore.ResolvePath(set.DataDirectory, diff.OsuFileHash);
            if (path == null)
                return;

            Task.Run(() =>
            {
                ParsedBeatmap parsed;
                try
                {
                    parsed = OsuFileDecoder.Decode(path);
                }
                catch
                {
                    return;
                }

                int circles = parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Circle);
                int sliders = parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Slider);
                int spinners = parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Spinner);

                string length = formatLength(parsed);
                string bpm = formatBpm(parsed);
                string cs = parsed.CircleSize.ToString("0.0");
                string ar = parsed.EffectiveApproachRate.ToString("0.0");
                string od = parsed.OverallDifficulty.ToString("0.0");
                string hp = parsed.HpDrainRate.ToString("0.0");

                Schedule(() =>
                {
                    if (mine != token)
                        return;

                    metaFlow.Children = new[]
                    {
                        chip(spriteIcon(FontAwesome.Regular.Clock), length, 84),
                        chip(spriteIcon(FontAwesome.Solid.Music), bpm, 110),
                        metaDivider(),
                        chip(new Circle { Size = new Vector2(icon_size) }, circles.ToString(), 60),
                        chip(new SliderGlyph(), sliders.ToString(), 60),
                        chip(new SpinnerGlyph(), spinners.ToString(), 60),
                    };

                    statsFlow.Children = new[]
                    {
                        stat("CS", cs),
                        stat("AR", ar),
                        stat("OD", od),
                        stat("HP", hp),
                    };
                });
            });
        }

        // --- Fixed-width chips (so values never reflow the row) ---

        /// <summary>A thin vertical hairline separating the meta groups on the info line.</summary>
        private static Drawable metaDivider() => new Box
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Width = 1,
            Height = 13,
            Colour = EditorTheme.Colours.Border,
        };

        /// <summary>A fixed-width chip: a muted icon/glyph followed by a value, left-aligned in a constant box.</summary>
        private static Drawable chip(Drawable iconDrawable, string value, float width)
        {
            iconDrawable.Anchor = Anchor.CentreLeft;
            iconDrawable.Origin = Anchor.CentreLeft;
            iconDrawable.Colour = EditorTheme.Colours.TextMuted;

            return new Container
            {
                AutoSizeAxes = Axes.Y,
                Width = width,
                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                    Children = new[]
                    {
                        iconDrawable,
                        valueText(value),
                    },
                },
            };
        }

        /// <summary>A fixed-width stat chip: a faint label and its value (e.g. "CS 4.0"), like a spec sheet.</summary>
        private static Drawable stat(string label, string value) => new Container
        {
            AutoSizeAxes = Axes.Y,
            Width = 58,
            Child = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Sm, 0),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Colour = EditorTheme.Colours.TextFaint,
                        Font = FontUsage.Default.With(size: 12, weight: "SemiBold"),
                    },
                    valueText(value),
                },
            },
        };

        private static SpriteText valueText(string value) => new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = value,
            Colour = EditorTheme.Colours.Text,
            Font = FontUsage.Default.With(size: 15, weight: "SemiBold", fixedWidth: true),
        };

        private static Drawable centred(Drawable d)
        {
            d.Anchor = Anchor.CentreLeft;
            d.Origin = Anchor.CentreLeft;
            return d;
        }

        private static SpriteIcon spriteIcon(IconUsage iconUsage) => new SpriteIcon
        {
            Icon = iconUsage,
            Size = new Vector2(icon_size),
        };

        // --- Custom hit-object glyphs (drawn, not font icons), tinted via the chip's Colour ---

        /// <summary>A capsule "body" with a solid head, reading as a slider.</summary>
        private partial class SliderGlyph : CompositeDrawable
        {
            public SliderGlyph()
            {
                Size = new Vector2(icon_size * 1.8f, icon_size);

                InternalChildren = new Drawable[]
                {
                    // Body: a rounded capsule outline spanning the full width.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = icon_size / 2f,
                        BorderThickness = 2,
                        BorderColour = Color4.White,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
                    // Head: a solid circle at the left.
                    new Circle { Size = new Vector2(icon_size) },
                };
            }
        }

        /// <summary>A hollow ring, reading as a spinner.</summary>
        private partial class SpinnerGlyph : CompositeDrawable
        {
            public SpinnerGlyph()
            {
                Size = new Vector2(icon_size);

                InternalChild = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = 2,
                    BorderColour = Color4.White,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                };
            }
        }

        private static string formatLength(ParsedBeatmap parsed)
        {
            if (parsed.HitObjects.Count == 0)
                return "0:00";

            double start = parsed.HitObjects.Min(o => o.StartTime);
            double end = parsed.HitObjects.Max(o => o.StartTime + o.Duration);
            var span = TimeSpan.FromMilliseconds(Math.Max(0, end - start));
            return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        }

        private static string formatBpm(ParsedBeatmap parsed)
        {
            var bpms = parsed.TimingPointModels
                .Where(t => t.Uninherited && t.Bpm > 0)
                .Select(t => (int)Math.Round(t.Bpm))
                .ToList();

            if (bpms.Count == 0)
                return "-- BPM";

            int min = bpms.Min(), max = bpms.Max();
            return min == max ? $"{min} BPM" : $"{min}-{max} BPM";
        }
    }
}
