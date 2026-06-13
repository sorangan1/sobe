using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A small inline editor that pops up beneath a clicked timeline timing-point pill. For a red (uninherited)
    /// point it edits the BPM and the offset (time); for a green (inherited) point it edits the slider-velocity
    /// multiplier and the hitsound volume. The first field is auto-focused; Enter commits, Escape / clicking
    /// away closes. Edits flow through <see cref="OnApply"/> (the editor's UpdateTimingPoint).
    /// </summary>
    public partial class TimingPillPopover : VisibilityContainer
    {
        /// <summary>Applies an edited timing point (same id) back to the beatmap.</summary>
        public Action<TimingPointModel>? OnApply;

        /// <summary>Deletes the timing point with the given id from the beatmap.</summary>
        public Action<int>? OnDelete;

        private Container panel = null!;
        private FillFlowContainer fields = null!;

        private TimingPointModel current;
        private readonly List<BasicTextBox> boxes = new List<BasicTextBox>();

        protected override bool StartHidden => true;

        public TimingPillPopover()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // A transparent full-screen catcher so clicking anywhere outside the panel dismisses it.
            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent },
                panel = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Md,
                    EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                    {
                        Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                        Colour = new Color4(0f, 0f, 0f, 0.5f),
                        Radius = 8f,
                    },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                        fields = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(EditorTheme.Spacing.Md),
                            Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                        },
                    },
                },
            };
        }

        /// <summary>Opens the editor for a timing point at the given (local) position, just below its pill.</summary>
        public void OpenFor(TimingPointModel tp, Vector2 localPosition)
        {
            current = tp;
            panel.Position = localPosition + new Vector2(0, 4);
            Show();
            // The first field auto-focuses itself once loaded (reliable, unlike a same-frame ChangeFocus).
            buildFields();
        }

        private void buildFields()
        {
            fields.Clear();
            boxes.Clear();

            bool red = current.Uninherited;
            fields.Add(header(red ? "Red — BPM / offset" : "Green — SV / volume",
                red ? EditorTheme.Colours.Timing : EditorTheme.Colours.Velocity));

            if (red)
            {
                var bpm = field("BPM", current.Bpm.ToString("0.###", CultureInfo.InvariantCulture));
                var offset = field("Offset (ms)", current.Time.ToString("0", CultureInfo.InvariantCulture));

                void commit()
                {
                    double newBpm = parse(bpm, current.Bpm);
                    double newTime = parse(offset, current.Time);
                    apply(current with { Time = newTime, BeatLength = TimingPointLineEditor.BeatLengthFromBpm(newBpm) });
                }

                bpm.OnCommit += (_, _) => commit();
                offset.OnCommit += (_, _) => commit();
            }
            else
            {
                var sv = field("SV multiplier", current.SliderVelocity.ToString("0.###", CultureInfo.InvariantCulture));
                var volume = field("Volume (%)", current.Volume.ToString(CultureInfo.InvariantCulture));

                void commit()
                {
                    double newSv = Math.Clamp(parse(sv, current.SliderVelocity), 0.1, 10);
                    int newVol = Math.Clamp((int)Math.Round(parse(volume, current.Volume)), 0, 100);
                    apply(current with { BeatLength = TimingPointLineEditor.BeatLengthFromSv(newSv), Volume = newVol });
                }

                sv.OnCommit += (_, _) => commit();
                volume.OnCommit += (_, _) => commit();
            }

            // A delete action right in the inline editor, so a point can be removed without opening the full F6 list.
            fields.Add(new OsuButton("Delete", EditorTheme.Colours.Error)
            {
                Width = 150,
                Height = EditorTheme.Sizing.ButtonHeight,
                FontSize = 12,
                Margin = new MarginPadding { Top = EditorTheme.Spacing.Xs },
                Action = () =>
                {
                    OnDelete?.Invoke(current.Id);
                    Hide();
                },
            });
        }

        private void apply(TimingPointModel updated)
        {
            OnApply?.Invoke(updated);
            current = updated;
            Hide();
        }

        private static double parse(BasicTextBox box, double fallback) =>
            double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static Drawable header(string text, Color4 colour) => new SpriteText
        {
            Text = text,
            Colour = colour,
            Font = EditorTheme.Type.Caption(),
        };

        private BasicTextBox field(string label, string value)
        {
            var box = new NumericTextBox
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.InputHeight,
                Text = value,
                AutoFocus = boxes.Count == 0, // the first field grabs focus on open
            };
            boxes.Add(box);

            fields.Add(new FillFlowContainer
            {
                Width = 150,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText { Text = label, Colour = EditorTheme.Colours.TextMuted, Font = EditorTheme.Type.Caption() },
                    box,
                },
            });

            return box;
        }

        protected override void PopIn() => this.FadeIn(120, Easing.OutQuint);

        protected override void PopOut()
        {
            this.FadeOut(100, Easing.OutQuint);
            GetContainingFocusManager()?.ChangeFocus(null);
        }

        protected override bool OnClick(ClickEvent e)
        {
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

        /// <summary>A text box accepting a signed decimal value; can auto-focus and select itself once loaded.</summary>
        private partial class NumericTextBox : BasicTextBox
        {
            public bool AutoFocus { get; init; }

            protected override bool CanAddCharacter(char character) => char.IsDigit(character) || character == '.' || character == '-';

            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (AutoFocus)
                    Schedule(() =>
                    {
                        GetContainingInputManager()?.ChangeFocus(this);
                        SelectAll();
                    });
            }
        }
    }
}
