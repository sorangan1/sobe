using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The editor playback control (a faithful port of osu!lazer's <c>PlaybackControl</c>): a play/pause
    /// button plus a set of speed options (100% / 75% / 50%). The speed adjusts the track <em>tempo</em>,
    /// so the pitch is preserved as it plays slower - identical to osu!lazer. Sits at the bottom-right,
    /// after the bottom timeline.
    /// </summary>
    public partial class PlaybackControl : CompositeDrawable
    {
        public const float WIDTH = 214f;

        // Speed options, slowest → fastest (osu!lazer's set plus 25%).
        private static readonly double[] tempo_values = { 0.25, 0.5, 0.75, 1 };

        private readonly Track? track;
        private readonly Func<bool> isPlaying;
        private readonly Action togglePlay;

        // A multiplicative tempo adjustment on the track (preserves pitch); 1 = full speed.
        private readonly BindableNumber<double> tempo = new BindableDouble(1);

        private PlayButton playButton = null!;
        private RateButton[] rateButtons = System.Array.Empty<RateButton>();
        private int rateIndex = tempo_values.Length - 1; // start at full speed (100%)

        public PlaybackControl(Track? track, Func<bool> isPlaying, Action togglePlay)
        {
            this.track = track;
            this.isPlaying = isPlaying;
            this.togglePlay = togglePlay;

            Width = WIDTH;
            Height = EditorTimeline.HEIGHT;
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            track?.AddAdjustment(AdjustableProperty.Tempo, tempo);

            var flow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Xs, 0),
                Children = new Drawable[] { playButton = new PlayButton(togglePlay) },
            };

            rateButtons = new RateButton[tempo_values.Length];
            for (int i = 0; i < tempo_values.Length; i++)
            {
                int index = i;
                var button = new RateButton(tempo_values[i], () => setRate(index));
                rateButtons[i] = button;
                flow.Add(button);
            }

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.BackgroundRaised, Alpha = 0.82f },
                flow,
            };

            setRate(rateIndex);
        }

        private void setRate(int index)
        {
            rateIndex = Math.Clamp(index, 0, tempo_values.Length - 1);
            tempo.Value = tempo_values[rateIndex];
            for (int i = 0; i < rateButtons.Length; i++)
                rateButtons[i].SetActive(i == rateIndex);
        }

        /// <summary>Steps to the next-faster speed option (no-op at 100%).</summary>
        public void IncreaseRate() => setRate(rateIndex + 1);

        /// <summary>Steps to the next-slower speed option (no-op at the slowest).</summary>
        public void DecreaseRate() => setRate(rateIndex - 1);

        protected override void Update()
        {
            base.Update();
            playButton.SetPlaying(isPlaying());
        }

        /// <summary>The play/pause square button (ASCII glyphs: ">" play, "||" pause).</summary>
        private partial class PlayButton : ClickableContainer
        {
            private Box background = null!;
            private SpriteText glyph = null!;
            private bool playing;

            public PlayButton(Action onClick)
            {
                Action = onClick;
                Size = new Vector2(30, 26);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    glyph = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = ">",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.BodyStrong(),
                    },
                };
            }

            public void SetPlaying(bool value)
            {
                if (value == playing)
                    return;

                playing = value;
                glyph.Text = playing ? "||" : ">";
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) =>
                background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }

        /// <summary>A speed option chip, labelled as a percentage; active = solid accent.</summary>
        private partial class RateButton : ClickableContainer
        {
            public double Rate { get; }

            private Box background = null!;
            private SpriteText text = null!;
            private bool active;

            public RateButton(double rate, Action onClick)
            {
                Rate = rate;
                Action = onClick;
                Size = new Vector2(40, 26);
                Masking = true;
                CornerRadius = EditorTheme.Radius.Md;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    text = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = $"{Rate:0%}",
                        Colour = EditorTheme.Colours.Text,
                        Font = EditorTheme.Type.Label(numeric: true),
                    },
                };
            }

            public void SetActive(bool value)
            {
                active = value;
                background.FadeColour(active ? EditorTheme.Colours.Accent : EditorTheme.Colours.Control, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                text.FadeColour(active ? EditorTheme.Colours.Sunken : EditorTheme.Colours.Text, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!active)
                    background.FadeColour(EditorTheme.Colours.ControlHover, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!active)
                    background.FadeColour(EditorTheme.Colours.Control, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            }
        }
    }
}
