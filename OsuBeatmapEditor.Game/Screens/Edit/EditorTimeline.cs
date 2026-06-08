using System;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Compact bottom transport bar: current time (MM:SS:XXX) followed by a seek bar that also shows
    /// bookmarks (blue, lower half), uninherited/inherited timing points (red/green, upper half) and
    /// the preview point (a small circle on the line). Marker colours come from <see cref="EditorSettings"/>.
    /// </summary>
    public partial class EditorTimeline : CompositeDrawable
    {
        public const float HEIGHT = 46;

        private readonly Track? track;
        private readonly ParsedBeatmap? beatmap;
        private readonly Func<double>? timeSource;

        private SpriteText timeText = null!;
        private SeekBar seekBar = null!;

        public EditorTimeline(Track? track, ParsedBeatmap? beatmap, Func<double>? timeSource = null)
        {
            this.track = track;
            this.beatmap = beatmap;
            this.timeSource = timeSource;

            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
        }

        private double currentTime => timeSource?.Invoke() ?? track?.CurrentTime ?? 0;

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.BackgroundRaised,
                },
                timeText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 16 },
                    Colour = OsuColour.Text,
                    Font = FontUsage.Default.With(size: 18, weight: "Bold", fixedWidth: true),
                    Text = "00:00:000",
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 130, Right = 24, Vertical = 9 },
                    Child = seekBar = new SeekBar(track, beatmap, () => currentTime),
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            if (track != null)
                timeText.Text = format(currentTime);
        }

        private static string format(double ms)
        {
            if (ms < 0 || double.IsNaN(ms))
                ms = 0;

            var t = TimeSpan.FromMilliseconds(ms);
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}:{t.Milliseconds:000}";
        }

        /// <summary>The seek bar with its timing/bookmark/preview markers.</summary>
        private partial class SeekBar : Container
        {
            [Resolved]
            private EditorSettings settings { get; set; } = null!;

            private readonly Track? track;
            private readonly ParsedBeatmap? beatmap;
            private readonly Func<double> timeSource;

            private Container kiaiLayer = null!;
            private Container markers = null!;
            private Box playhead = null!;
            private bool markersBuilt;

            public SeekBar(Track? track, ParsedBeatmap? beatmap, Func<double> timeSource)
            {
                this.track = track;
                this.beatmap = beatmap;
                this.timeSource = timeSource;
                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    // Kiai bands sit behind everything.
                    kiaiLayer = new Container { RelativeSizeAxes = Axes.Both },
                    // The horizontal time line itself.
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 3,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = OsuColour.Surface,
                    },
                    markers = new Container { RelativeSizeAxes = Axes.Both },
                    playhead = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 2,
                        RelativePositionAxes = Axes.X,
                        Origin = Anchor.TopCentre,
                        Anchor = Anchor.TopLeft,
                        Colour = OsuColour.Text,
                    },
                };
            }

            protected override void Update()
            {
                base.Update();

                if (track == null || track.Length <= 0)
                    return;

                if (!markersBuilt)
                    buildMarkers();

                playhead.X = (float)(timeSource() / track.Length);
            }

            private void buildMarkers()
            {
                markersBuilt = true;

                if (beatmap == null || track == null || track.Length <= 0)
                    return;

                double length = track.Length;

                foreach (var kiai in beatmap.KiaiSections)
                {
                    double startFraction = kiai.Start / length;
                    double endFraction = kiai.End == int.MaxValue ? 1.0 : kiai.End / length;
                    kiaiLayer.Add(kiaiBand(startFraction, endFraction - startFraction));
                }

                foreach (var tp in beatmap.TimingPoints)
                    markers.Add(verticalMarker(tp.Time / length, top: true,
                        tp.Uninherited ? settings.UninheritedColour : settings.InheritedColour));

                foreach (int bookmark in beatmap.Bookmarks)
                    markers.Add(verticalMarker(bookmark / length, top: false, settings.BookmarkColour));

                if (beatmap.PreviewTime >= 0)
                    markers.Add(previewMarker(beatmap.PreviewTime / length));
            }

            private Drawable verticalMarker(double fraction, bool top, Bindable<Colour4> colour)
            {
                var box = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Height = 0.5f,
                    Width = 2,
                    RelativePositionAxes = Axes.X,
                    X = (float)fraction,
                    Anchor = top ? Anchor.TopLeft : Anchor.BottomLeft,
                    Origin = top ? Anchor.TopCentre : Anchor.BottomCentre,
                };

                colour.BindValueChanged(c => box.Colour = c.NewValue, true);
                return box;
            }

            private Drawable kiaiBand(double startFraction, double widthFraction)
            {
                var box = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Height = 0.5f, // centred on the line - neither up nor down
                    RelativePositionAxes = Axes.X,
                    X = (float)startFraction,
                    Width = (float)widthFraction,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                };

                // Always 40% opacity, regardless of the chosen colour.
                settings.KiaiColour.BindValueChanged(c => box.Colour = new Colour4(c.NewValue.R, c.NewValue.G, c.NewValue.B, 0.4f), true);
                return box;
            }

            private Drawable previewMarker(double fraction)
            {
                var circle = new Circle
                {
                    Size = new Vector2(9),
                    RelativePositionAxes = Axes.X,
                    X = (float)fraction,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                };

                settings.PreviewPointColour.BindValueChanged(c => circle.Colour = c.NewValue, true);
                return circle;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                seekTo(e);
                return true;
            }

            protected override bool OnDragStart(DragStartEvent e) => true;

            protected override void OnDrag(DragEvent e) => seekTo(e);

            private void seekTo(MouseButtonEvent e)
            {
                if (track == null || track.Length <= 0)
                    return;

                float fraction = Math.Clamp(ToLocalSpace(e.ScreenSpaceMousePosition).X / DrawWidth, 0, 1);
                track.Seek(fraction * track.Length);
            }
        }
    }
}
