using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// osu!lazer-style volume popup with master / music / effects sliders, bound to the framework's
    /// audio manager. Appears on scroll and auto-hides; hovering keeps it open.
    /// </summary>
    public partial class VolumeControl : CompositeDrawable
    {
        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private ScheduledDelegate? hideDelegate;

        public VolumeControl()
        {
            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;
            AutoSizeAxes = Axes.Both;
            Margin = new MarginPadding { Bottom = 24 };
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = OsuColour.BackgroundRaised,
                        Alpha = 0.95f,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(20, 0),
                        Padding = new MarginPadding { Horizontal = 24, Vertical = 16 },
                        Children = new Drawable[]
                        {
                            new VolumeMeter("Master", audio.Volume, show),
                            new VolumeMeter("Music", audio.VolumeTrack, show),
                            new VolumeMeter("Effects", audio.VolumeSample, show),
                        },
                    },
                },
            };
        }

        /// <summary>Nudges master volume (used by the global scroll handler) and reveals the popup.</summary>
        public void AdjustMaster(float delta)
        {
            audio.Volume.Value = Math.Clamp(audio.Volume.Value + delta * 0.05, 0, 1);
            show();
        }

        private void show()
        {
            this.FadeIn(150, Easing.OutQuint);
            scheduleHide();
        }

        private void scheduleHide()
        {
            hideDelegate?.Cancel();
            hideDelegate = Scheduler.AddDelayed(() => this.FadeOut(300, Easing.OutQuint), 1500);
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            AdjustMaster(e.ScrollDelta.Y);
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            hideDelegate?.Cancel();
            this.FadeIn(150, Easing.OutQuint);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            scheduleHide();
            base.OnHoverLost(e);
        }

        /// <summary>A single labelled volume slider. Scrolling over it adjusts that channel.</summary>
        private partial class VolumeMeter : CompositeDrawable
        {
            private readonly string label;
            private readonly BindableNumber<double> current;
            private readonly Action onInteract;

            public VolumeMeter(string label, BindableNumber<double> current, Action onInteract)
            {
                this.label = label;
                this.current = current;
                this.onInteract = onInteract;

                Width = 190;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = label,
                            Colour = OsuColour.TextMuted,
                            Font = FontUsage.Default.With(size: 15, weight: "SemiBold"),
                        },
                        new BasicSliderBar<double>
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 22,
                            Current = current.GetBoundCopy(),
                            BackgroundColour = OsuColour.Surface,
                            SelectionColour = OsuColour.Pink,
                        },
                    },
                };
            }

            protected override bool OnScroll(ScrollEvent e)
            {
                current.Value = Math.Clamp(current.Value + e.ScrollDelta.Y * 0.05, 0, 1);
                onInteract();
                return true;
            }
        }
    }
}
