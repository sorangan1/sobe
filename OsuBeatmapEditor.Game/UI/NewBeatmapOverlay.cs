using System;
using System.Globalization;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Modal dialog for creating a new (empty) beatmap: title, artist, creator, difficulty name,
    /// BPM and an audio file. On confirm it builds a standard .osz and hands it to osu!lazer.
    /// </summary>
    public partial class NewBeatmapOverlay : VisibilityContainer
    {
        /// <summary>Allowed audio extensions for the source track (lower-case, with leading dot).</summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".ogg", ".wav" };

        /// <summary>Invoked after a beatmap has been successfully built and sent to osu!lazer.</summary>
        public Action<NewBeatmapRequest>? Created;

        private FormField artistField = null!;
        private FormField titleField = null!;
        private FormField creatorField = null!;
        private FormField difficultyField = null!;
        private FormField bpmField = null!;
        private BasicFileSelector fileSelector = null!;
        private Box audioChipBackground = null!;
        private SpriteText audioChipText = null!;
        private SpriteText statusText = null!;
        private OsuButton createButton = null!;

        private Container panel = null!;
        private string? audioPath;

        // Set when seeding from an existing set ("Create new Set"): reuse its audio filename and timing.
        private string? seededAudioFileName;
        private System.Collections.Generic.IReadOnlyList<string>? seededTimingLines;

        protected override bool StartHidden => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = osuTK.Graphics.Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new ClickBlockingContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(EditorTheme.Sizing.OverlayWidth, EditorTheme.Sizing.OverlayHeight),
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Raised,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(EditorTheme.Spacing.Xxl),
                            Child = new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),                 // header
                                    new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Xl),
                                    new Dimension(),                                       // body (fills)
                                    new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Lg),
                                    new Dimension(GridSizeMode.AutoSize),                 // footer
                                },
                                Content = new[]
                                {
                                    new Drawable?[] { buildHeader() },
                                    new Drawable?[] { null },
                                    new Drawable?[] { buildBody() },
                                    new Drawable?[] { null },
                                    new Drawable?[] { buildFooter() },
                                },
                            },
                        },
                    },
                },
            };

            createButton.Enabled.Value = false;

            artistField.Current.ValueChanged += _ => validate();
            titleField.Current.ValueChanged += _ => validate();
            bpmField.Current.ValueChanged += _ => validate();

            fileSelector.CurrentFile.BindValueChanged(e =>
            {
                if (e.NewValue == null)
                    return;

                // Picking a file overrides any seeded audio; its extension now derives from the chosen path.
                setAudio(e.NewValue.FullName);
            });
        }

        private Drawable buildHeader() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, EditorTheme.Spacing.Xs),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = "New Beatmap",
                    Colour = EditorTheme.Colours.Accent,
                    Font = EditorTheme.Type.Display(),
                },
                new SpriteText
                {
                    Text = "Fill in the metadata and choose an audio track to start from.",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
            },
        };

        private Drawable buildBody() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 320),
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Xxl),
                new Dimension(),
            },
            Content = new[]
            {
                new Drawable?[]
                {
                    // Left column: metadata fields.
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Lg),
                        Children = new Drawable[]
                        {
                            artistField = new FormField("Artist"),
                            titleField = new FormField("Title"),
                            creatorField = new FormField("Creator"),
                            difficultyField = new FormField("Difficulty name", "Normal"),
                            bpmField = new FormField("BPM", "120"),
                        },
                    },
                    null,
                    // Right column: audio track selection.
                    buildAudioColumn(),
                },
            },
        };

        private Drawable buildAudioColumn() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = new[]
            {
                new Dimension(GridSizeMode.AutoSize),                 // heading
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Sm),
                new Dimension(GridSizeMode.AutoSize),                 // selected-file chip
                new Dimension(GridSizeMode.Absolute, EditorTheme.Spacing.Sm),
                new Dimension(),                                       // file browser (fills)
            },
            Content = new[]
            {
                new Drawable?[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, EditorTheme.Spacing.Xxs),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = "Audio track",
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.Heading(),
                            },
                            new SpriteText
                            {
                                Text = "Drag a file onto the window, or browse below  (mp3 / ogg / wav)",
                                Colour = EditorTheme.Colours.TextFaint,
                                Font = EditorTheme.Type.Caption(),
                            },
                        },
                    },
                },
                new Drawable?[] { null },
                new Drawable?[] { buildAudioChip() },
                new Drawable?[] { null },
                new Drawable?[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = EditorTheme.Radius.Md,
                        BorderThickness = EditorTheme.Sizing.BorderThickness,
                        BorderColour = EditorTheme.Colours.Border,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = EditorTheme.Colours.Sunken,
                            },
                            fileSelector = new BasicFileSelector(
                                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                AudioExtensions)
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                        },
                    },
                },
            },
        };

        private Drawable buildAudioChip() => new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = EditorTheme.Sizing.RowHeight,
            Masking = true,
            CornerRadius = EditorTheme.Radius.Sm,
            Children = new Drawable[]
            {
                audioChipBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = EditorTheme.Colours.Control,
                },
                audioChipText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Horizontal = EditorTheme.Spacing.Md },
                    Text = "No audio selected",
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.BodyStrong(),
                    Truncate = true,
                    RelativeSizeAxes = Axes.X,
                },
            },
        };

        private Drawable buildFooter() => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                statusText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Colour = EditorTheme.Colours.TextMuted,
                    Font = EditorTheme.Type.Body(),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                    Children = new Drawable[]
                    {
                        new OsuButton("Cancel", OsuColour.Surface)
                        {
                            Size = new Vector2(120, 44),
                            Action = () => Hide(),
                        },
                        createButton = new OsuButton("Create", OsuColour.Pink)
                        {
                            Size = new Vector2(150, 44),
                            Action = onCreate,
                        },
                    },
                },
            },
        };

        private NewBeatmapRequest buildRequest() => new NewBeatmapRequest
        {
            Artist = artistField.Current.Value.Trim(),
            Title = titleField.Current.Value.Trim(),
            Creator = creatorField.Current.Value.Trim(),
            DifficultyName = string.IsNullOrWhiteSpace(difficultyField.Current.Value) ? "Normal" : difficultyField.Current.Value.Trim(),
            Bpm = double.TryParse(bpmField.Current.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) ? bpm : 0,
            AudioPath = audioPath ?? string.Empty,
            AudioFileName = seededAudioFileName,
            TimingPointLines = seededTimingLines,
        };

        /// <summary>Opens the dialog for a brand-new map (clears any seeded audio/timing from a prior "new set").</summary>
        public void ShowForNewBeatmap()
        {
            seededAudioFileName = null;
            seededTimingLines = null;
            Show();
        }

        /// <summary>
        /// Opens the dialog for a brand-new map with an audio file already chosen (e.g. dropped onto the
        /// window). Artist/Title are guessed from the filename when they fit the "Artist - Title" convention.
        /// </summary>
        public void ShowForDroppedAudio(string path)
        {
            seededTimingLines = null;
            setAudio(path);

            string stem = Path.GetFileNameWithoutExtension(path);
            int dash = stem.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                if (artistField.Current.Value.Length == 0)
                    artistField.Current.Value = stem[..dash].Trim();
                if (titleField.Current.Value.Length == 0)
                    titleField.Current.Value = stem[(dash + 3)..].Trim();
            }
            else if (titleField.Current.Value.Length == 0)
            {
                titleField.Current.Value = stem.Trim();
            }

            Show();
        }

        /// <summary>
        /// Opens the dialog pre-filled from an existing set ("Create new Set"): metadata, plus its audio file
        /// and timing points carried over. The user can still adjust any field or pick a different audio file.
        /// </summary>
        public void ShowSeeded(string artist, string title, string creator, string audioStorePath,
                               string audioFileName, System.Collections.Generic.IReadOnlyList<string> timingLines, double bpm)
        {
            artistField.Current.Value = artist;
            titleField.Current.Value = title;
            creatorField.Current.Value = creator;
            difficultyField.Current.Value = "Normal";
            if (bpm > 0)
                bpmField.Current.Value = bpm.ToString("0.###", CultureInfo.InvariantCulture);

            audioPath = audioStorePath;
            seededAudioFileName = audioFileName;
            seededTimingLines = timingLines;
            setAudioLabel($"Reusing audio: {audioFileName}");

            validate();
            Show();
        }

        /// <summary>Records a freshly chosen source audio path (overriding any seeded audio) and updates the UI.</summary>
        private void setAudio(string path)
        {
            audioPath = path;
            seededAudioFileName = null;
            setAudioLabel(Path.GetFileName(path));
            validate();
        }

        private void setAudioLabel(string text)
        {
            bool hasAudio = audioPath != null;
            audioChipText.Text = text;
            audioChipText.Colour = hasAudio ? EditorTheme.Colours.Text : EditorTheme.Colours.TextMuted;
            audioChipBackground.FadeColour(hasAudio ? EditorTheme.Colours.AccentSoft : EditorTheme.Colours.Control,
                EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        private void validate() => createButton.Enabled.Value = buildRequest().IsValid;

        private void onCreate()
        {
            var request = buildRequest();
            if (!request.IsValid)
                return;

            try
            {
                string osz = BeatmapArchiveWriter.Write(request);
                bool launched = LazerImporter.Import(osz);

                statusText.Colour = launched ? EditorTheme.Colours.Success : EditorTheme.Colours.Error;
                statusText.Text = launched
                    ? "Sent to osu!lazer - check your library."
                    : "Could not launch osu!lazer.";

                if (launched)
                {
                    Created?.Invoke(request);
                    Scheduler.AddDelayed(Hide, 1200);
                }
            }
            catch (Exception ex)
            {
                statusText.Colour = EditorTheme.Colours.Error;
                statusText.Text = $"Failed: {ex.Message}";
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.ScaleTo(1, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
            panel.MoveToY(0, EditorTheme.Motion.Slow, EditorTheme.Motion.Ease);
        }

        protected override void PopOut()
        {
            this.FadeOut(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.ScaleTo(0.96f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            panel.MoveToY(20, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Click outside the panel (children of the panel consume their own clicks) dismisses.
            Hide();
            return true;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>A vertical label + text box pair used for each form field.</summary>
        private partial class FormField : FillFlowContainer
        {
            private readonly string label;
            private readonly string placeholder;
            private BasicTextBox textBox = null!;

            public Bindable<string> Current => textBox.Current;

            public FormField(string label, string placeholder = "")
            {
                this.label = label;
                this.placeholder = placeholder;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, EditorTheme.Spacing.Xs);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Label(),
                    },
                    textBox = new BasicTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = EditorTheme.Sizing.InputHeight,
                        PlaceholderText = placeholder,
                        Text = placeholder,
                    },
                };
            }
        }

        /// <summary>Container that swallows mouse input so clicks inside the panel don't dismiss the overlay.</summary>
        private partial class ClickBlockingContainer : Container
        {
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnMouseDown(MouseDownEvent e) => true;
        }
    }
}
