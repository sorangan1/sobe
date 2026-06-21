using System;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// Top-left readout for the selected map, composed as a few distinct blocks rather than one flat list:
    /// a title block (artist/title), a difficulty row (a difficulty-tinted star tab + the difficulty name on
    /// the left, a small mapper card on the right), and a separate "spec" sub-panel grouping the song meta
    /// (length/BPM/object counts) above the CS/AR/OD/HP gauges. Metadata appears instantly on selection;
    /// counts and gauges fill in after the .osu is decoded off the update thread.
    /// </summary>
    public partial class BeatmapInfoPanel : CompositeDrawable
    {
        private const float content_width = 500;
        private const float panel_inset = EditorTheme.Spacing.Xl;
        private const float inner_width = content_width - 2 * panel_inset;
        private const float icon_size = 13;

        private SpriteText title = null!;
        private SpriteText artist = null!;
        private SpriteText difficultyName = null!;
        private SpriteText mapperName = null!;
        private SpriteText starValue = null!;
        private SpriteIcon starIcon = null!;
        private Box starTabBg = null!;
        private Container mapperAvatar = null!;
        private SpriteIcon mapperPlaceholder = null!;
        private FillFlowContainer metaFlow = null!;
        private FillFlowContainer statsFlow = null!;

        private TextureStore? onlineTextures;
        private Color4 accentColour = EditorTheme.Colours.Accent;

        // Guards against a stale async decode / avatar load overwriting a newer selection.
        private int token;

        [BackgroundDependencyLoader]
        private void load(OnlineTextureStore onlineTextures)
        {
            this.onlineTextures = onlineTextures;

            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = EditorTheme.Radius.Lg;
            BorderThickness = 1;
            BorderColour = EditorTheme.Colours.Border;

            InternalChildren = new Drawable[]
            {
                // A subtle top-to-bottom gradient instead of a flat fill, so the panel has some depth.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    BypassAutoSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        EditorTheme.Colours.Base.Opacity(0.9f),
                        EditorTheme.Colours.Sunken.Opacity(0.94f)),
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Y,
                    Width = content_width,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                    Padding = new MarginPadding { Horizontal = panel_inset, Vertical = EditorTheme.Spacing.Lg },
                    Children = new Drawable[]
                    {
                        buildTitleBlock(),
                        buildDifficultyRow(),
                        buildSpecPanel(),
                    },
                },
            };
        }

        // --- Composition blocks ---------------------------------------------------------------------

        /// <summary>The credit block: the artist as a small eyebrow over the bold title.</summary>
        private Drawable buildTitleBlock() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Xxs),
            Children = new Drawable[]
            {
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
                    Font = FontUsage.Default.With(size: 28, weight: "Bold"),
                },
            },
        };

        /// <summary>The difficulty row: star tab + difficulty name on the left, the mapper card on the right.</summary>
        private Drawable buildDifficultyRow() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                    Children = new Drawable[]
                    {
                        buildStarTab(),
                        difficultyName = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Truncate = true,
                            MaxWidth = 220,
                            Colour = EditorTheme.Colours.Text,
                            Font = FontUsage.Default.With(size: 18, weight: "SemiBold"),
                        },
                    },
                },
                buildMapperCard(),
            },
        };

        /// <summary>The difficulty-tinted star-rating tab (a star glyph + the numeric rating).</summary>
        private Drawable buildStarTab() => new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = EditorTheme.Radius.Sm,
            Children = new Drawable[]
            {
                starTabBg = new Box { RelativeSizeAxes = Axes.Both, Colour = accentColour.Opacity(0.18f) },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                    Padding = new MarginPadding { Horizontal = EditorTheme.Spacing.Md, Vertical = 4 },
                    Children = new Drawable[]
                    {
                        starIcon = new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.Star,
                            Size = new Vector2(11),
                            Colour = accentColour,
                        },
                        starValue = new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "0.00",
                            Colour = EditorTheme.Colours.Text,
                            Font = FontUsage.Default.With(size: 14, weight: "Bold", fixedWidth: true),
                        },
                    },
                },
            },
        };

        /// <summary>A small "mapped by" card (its own surface): the mapper's avatar and name, right-aligned.</summary>
        private Drawable buildMapperCard()
        {
            mapperPlaceholder = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.User,
                Size = new Vector2(11),
                Colour = EditorTheme.Colours.TextFaint,
            };

            mapperAvatar = new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(26),
                Masking = true,
                CornerRadius = EditorTheme.Radius.Sm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    mapperPlaceholder,
                },
            };

            return new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                        Padding = new MarginPadding { Left = 4, Right = EditorTheme.Spacing.Lg, Vertical = 4 },
                        Children = new Drawable[]
                        {
                            mapperAvatar,
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Children = new Drawable[]
                                {
                                    new SpriteText
                                    {
                                        Text = "MAPPED BY",
                                        Colour = EditorTheme.Colours.TextFaint,
                                        Font = FontUsage.Default.With(size: 9, weight: "Bold"),
                                    },
                                    mapperName = new SpriteText
                                    {
                                        Text = "unknown",
                                        Colour = EditorTheme.Colours.Text,
                                        Font = FontUsage.Default.With(size: 13, weight: "SemiBold"),
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>The "spec" sub-panel: a raised surface grouping the song meta over the difficulty gauges.</summary>
        private Drawable buildSpecPanel() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = EditorTheme.Radius.Md,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Surface.Opacity(0.85f) },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, EditorTheme.Spacing.Md),
                    Padding = new MarginPadding(EditorTheme.Spacing.Lg),
                    Children = new Drawable[]
                    {
                        // Length / BPM / object counts.
                        metaFlow = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.X,
                            Height = 20,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = EditorTheme.Colours.Border,
                        },
                        // CS / AR / OD / HP as little filled gauges, spread across the panel.
                        statsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Md, 0),
                        },
                    },
                },
            },
        };

        /// <summary>Shows the given map; decodes the .osu asynchronously to fill in counts and the gauges.</summary>
        public void SetMap(BeatmapSetModel set, BeatmapDifficultyModel diff)
        {
            int mine = ++token;

            title.Text = set.Title;
            artist.Text = set.Artist;
            difficultyName.Text = diff.DifficultyName;
            mapperName.Text = string.IsNullOrEmpty(set.Author) ? "unknown" : set.Author;

            // Tint the star tab to the difficulty (known synchronously from the stored rating).
            accentColour = StarRatingColour.For(diff.StarRating);
            starTabBg.FadeColour(accentColour.Opacity(0.18f), EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            starIcon.FadeColour(accentColour, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            starValue.Text = diff.StarRating.ToString("0.00");

            loadMapperAvatar(set.AuthorOnlineId, mine);

            metaFlow.Clear();
            statsFlow.Clear();

            string? path = LazerFileStore.ResolvePath(set.DataDirectory, diff.OsuFileHash);
            if (path == null)
                return;

            string hash = diff.OsuFileHash;

            Task.Run(() =>
            {
                if (!tryGetStats(hash, path, out var stats))
                    return;

                Schedule(() =>
                {
                    if (mine != token)
                        return;

                    metaFlow.Children = new[]
                    {
                        chip(spriteIcon(FontAwesome.Regular.Clock), stats.Length, 84),
                        chip(spriteIcon(FontAwesome.Solid.Music), stats.Bpm, 110),
                        metaDivider(),
                        chip(new Circle { Size = new Vector2(icon_size) }, stats.Circles.ToString(), 58),
                        chip(new SliderGlyph(), stats.Sliders.ToString(), 58),
                        chip(new SpinnerGlyph(), stats.Spinners.ToString(), 58),
                    };

                    statsFlow.Children = new[]
                    {
                        gauge("CS", stats.Cs),
                        gauge("AR", stats.Ar),
                        gauge("OD", stats.Od),
                        gauge("HP", stats.Hp),
                    };
                });
            });
        }

        /// <summary>Loads the mapper's osu! avatar into the card (or leaves the placeholder if unknown).</summary>
        private void loadMapperAvatar(int onlineId, int mine)
        {
            // Drop any previously-loaded avatar and reset the placeholder for the new selection.
            while (mapperAvatar.Count > 2)
                mapperAvatar.Remove(mapperAvatar.Children[^1], true);
            mapperPlaceholder.Alpha = 1;

            // OnlineID 1 is osu!lazer's "unknown user" sentinel; only real users have avatars.
            if (onlineId <= 1 || onlineTextures == null)
                return;

            LoadComponentAsync(new RemoteImage($"https://a.ppy.sh/{onlineId}", onlineTextures), img =>
            {
                if (mine != token)
                {
                    img.Expire();
                    return;
                }

                mapperAvatar.Add(img);
                img.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                mapperPlaceholder.FadeOut(EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            });
        }

        // --- Meta chips (fixed-width so values never reflow the row) ---

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

        private static SpriteText valueText(string value) => new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = value,
            Colour = EditorTheme.Colours.Text,
            Font = FontUsage.Default.With(size: 15, weight: "SemiBold", fixedWidth: true),
        };

        // --- Difficulty-setting gauges (label + value over a small filled track) ---

        /// <summary>One CS/AR/OD/HP gauge: the label and value above a thin bar filled to value/10.</summary>
        private Drawable gauge(string label, float value)
        {
            float fraction = Math.Clamp(value / 10f, 0f, 1f);

            return new Container
            {
                // Even quarter of the row (minus the inter-gauge spacing).
                Width = (inner_width - 2 * EditorTheme.Spacing.Lg - 3 * EditorTheme.Spacing.Md) / 4f,
                AutoSizeAxes = Axes.Y,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 14,
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = label,
                                    Colour = EditorTheme.Colours.TextFaint,
                                    Font = FontUsage.Default.With(size: 11, weight: "Bold"),
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Text = value.ToString("0.0"),
                                    Colour = EditorTheme.Colours.Text,
                                    Font = FontUsage.Default.With(size: 13, weight: "SemiBold", fixedWidth: true),
                                },
                            },
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 4,
                            Masking = true,
                            CornerRadius = 2,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = fraction,
                                    Colour = accentColour,
                                },
                            },
                        },
                    },
                },
            };
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
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = icon_size / 2f,
                        BorderThickness = 2,
                        BorderColour = Color4.White,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                    },
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

        /// <summary>The cheap, display-ready figures decoded from a difficulty's .osu (counts, length, BPM, stats).</summary>
        private readonly record struct MapStats(int Circles, int Sliders, int Spinners, string Length, string Bpm,
            float Cs, float Ar, float Od, float Hp);

        // Decoding a .osu to compute these is wasted work when the user hops back to a difficulty they've already
        // viewed (e.g. scrolling the carousel up and down). Cache by content hash; the hash changes on any edit,
        // so a stale entry can never be served. Capped so a marathon browsing session doesn't grow it without bound.
        private const int stats_cache_cap = 256;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MapStats> statsCache = new();

        private static bool tryGetStats(string hash, string path, out MapStats stats)
        {
            if (!string.IsNullOrEmpty(hash) && statsCache.TryGetValue(hash, out stats))
                return true;

            ParsedBeatmap parsed;
            try
            {
                parsed = OsuFileDecoder.Decode(path);
            }
            catch
            {
                stats = default;
                return false;
            }

            stats = new MapStats(
                parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Circle),
                parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Slider),
                parsed.HitObjects.Count(o => o.Kind == HitObjectKind.Spinner),
                formatLength(parsed),
                formatBpm(parsed),
                (float)parsed.CircleSize,
                (float)parsed.EffectiveApproachRate,
                (float)parsed.OverallDifficulty,
                (float)parsed.HpDrainRate);

            if (!string.IsNullOrEmpty(hash))
            {
                if (statsCache.Count >= stats_cache_cap)
                    statsCache.Clear();
                statsCache[hash] = stats;
            }

            return true;
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
