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
        /// <summary>Invoked after a beatmap has been successfully built and sent to osu!lazer.</summary>
        public Action<NewBeatmapRequest>? Created;

        private FormField artistField = null!;
        private FormField titleField = null!;
        private FormField creatorField = null!;
        private FormField difficultyField = null!;
        private FormField bpmField = null!;
        private BasicFileSelector fileSelector = null!;
        private SpriteText selectedAudioText = null!;
        private SpriteText statusText = null!;
        private OsuButton createButton = null!;

        private Container panel = null!;
        private string? audioPath;

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
                    Size = new Vector2(780, 560),
                    Masking = true,
                    CornerRadius = 12,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = OsuColour.BackgroundRaised,
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding(28),
                            Children = new Drawable[]
                            {
                                new SpriteText
                                {
                                    Text = "New Beatmap",
                                    Colour = OsuColour.Pink,
                                    Font = FontUsage.Default.With(size: 32, weight: "Bold"),
                                },
                                // Left column: text fields.
                                new FillFlowContainer
                                {
                                    Width = 330,
                                    AutoSizeAxes = Axes.Y,
                                    Margin = new MarginPadding { Top = 56 },
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 14),
                                    Children = new Drawable[]
                                    {
                                        artistField = new FormField("Artist"),
                                        titleField = new FormField("Title"),
                                        creatorField = new FormField("Creator"),
                                        difficultyField = new FormField("Difficulty name", "Normal"),
                                        bpmField = new FormField("BPM", "120"),
                                    },
                                },
                                // Right column: audio file selector.
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Top = 56, Left = 354, Bottom = 70 },
                                    Children = new Drawable[]
                                    {
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 8),
                                            Children = new Drawable[]
                                            {
                                                new SpriteText
                                                {
                                                    Text = "Audio file  (mp3 / ogg / wav)",
                                                    Colour = OsuColour.TextMuted,
                                                    Font = FontUsage.Default.With(size: 15),
                                                },
                                                new Container
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Height = 1,
                                                    Masking = true,
                                                    CornerRadius = 6,
                                                    Children = new Drawable[]
                                                    {
                                                        new Box
                                                        {
                                                            RelativeSizeAxes = Axes.Both,
                                                            Colour = OsuColour.BackgroundDark,
                                                        },
                                                        fileSelector = new BasicFileSelector(
                                                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                                            new[] { ".mp3", ".ogg", ".wav" })
                                                        {
                                                            RelativeSizeAxes = Axes.Both,
                                                        },
                                                    },
                                                },
                                            },
                                        },
                                        selectedAudioText = new SpriteText
                                        {
                                            Anchor = Anchor.BottomLeft,
                                            Origin = Anchor.BottomLeft,
                                            Margin = new MarginPadding { Bottom = -46 },
                                            Text = "No audio selected",
                                            Colour = OsuColour.TextMuted,
                                            Font = FontUsage.Default.With(size: 14),
                                        },
                                    },
                                },
                                // Footer: status + actions.
                                statusText = new SpriteText
                                {
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Colour = OsuColour.TextMuted,
                                    Font = FontUsage.Default.With(size: 15),
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.BottomRight,
                                    Origin = Anchor.BottomRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(12, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuButton("Cancel", OsuColour.Surface)
                                        {
                                            Size = new Vector2(130, 48),
                                            Action = () => Hide(),
                                        },
                                        createButton = new OsuButton("Create", OsuColour.Pink)
                                        {
                                            Size = new Vector2(160, 48),
                                            Action = onCreate,
                                        },
                                    },
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
                audioPath = e.NewValue?.FullName;
                selectedAudioText.Text = audioPath == null ? "No audio selected" : $"Selected: {Path.GetFileName(audioPath)}";
                validate();
            });
        }

        private NewBeatmapRequest buildRequest() => new NewBeatmapRequest
        {
            Artist = artistField.Current.Value.Trim(),
            Title = titleField.Current.Value.Trim(),
            Creator = creatorField.Current.Value.Trim(),
            DifficultyName = string.IsNullOrWhiteSpace(difficultyField.Current.Value) ? "Normal" : difficultyField.Current.Value.Trim(),
            Bpm = double.TryParse(bpmField.Current.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) ? bpm : 0,
            AudioPath = audioPath ?? string.Empty,
        };

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

                statusText.Colour = launched ? OsuColour.Pink : osuTK.Graphics.Color4.OrangeRed;
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
                statusText.Colour = osuTK.Graphics.Color4.OrangeRed;
                statusText.Text = $"Failed: {ex.Message}";
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(200, Easing.OutQuint);
            panel.ScaleTo(1, 400, Easing.OutQuint);
            panel.MoveToY(0, 400, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(200, Easing.OutQuint);
            panel.ScaleTo(0.96f, 200, Easing.OutQuint);
            panel.MoveToY(20, 200, Easing.OutQuint);
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
                Spacing = new Vector2(0, 4);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Colour = OsuColour.TextMuted,
                        Font = FontUsage.Default.With(size: 14),
                    },
                    textBox = new BasicTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 36,
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
