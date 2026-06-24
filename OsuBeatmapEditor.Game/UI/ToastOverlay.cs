using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Threading;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A lightweight transient-notification layer: brief "toasts" that slide in at the top of the screen to confirm
    /// an action, then auto-dismiss. Each carries an icon identifying the action and an accent colour for its type.
    /// Repeating the same action doesn't pile up copies - the existing toast is bumped with a "xN" counter and its
    /// timer reset - and the stack is capped so a spammed action can never flood the screen. Place it in front of
    /// the screen's content.
    /// </summary>
    public partial class ToastOverlay : CompositeDrawable
    {
        // Beyond this many visible toasts the oldest is dismissed early, so the stack can't run off-screen.
        private const int max_toasts = 4;

        private FillFlowContainer flow = null!;

        // The most recently shown toast; an identical message bumps this instead of adding a new one.
        private Toast? newest;

        public ToastOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = flow = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, EditorTheme.Spacing.Sm),
                Margin = new MarginPadding { Top = 88 },
            };
        }

        /// <summary>
        /// Shows a toast. <paramref name="accent"/> defaults to the neutral info colour; <paramref name="icon"/>
        /// defaults to an icon inferred from the accent (check / warning / error / info).
        /// </summary>
        public void Push(string message, Color4? accent = null, IconUsage? icon = null)
        {
            Color4 a = accent ?? EditorTheme.Colours.Info;

            // Spam guard: an identical, still-living message just bumps the existing toast (count + reset timer).
            if (newest != null && newest.Message == message)
            {
                newest.Bump();
                return;
            }

            var toast = new Toast(message, a, icon ?? defaultIcon(a));
            newest = toast;
            toast.Dismissed += () =>
            {
                if (newest == toast)
                    newest = null;
            };

            flow.Add(toast);

            // Cap the stack: dismiss the oldest still-present toasts beyond the limit.
            var live = flow.Children.OfType<Toast>().ToList();
            for (int i = 0; i < live.Count - max_toasts; i++)
                live[i].DismissNow();
        }

        /// <summary>The icon used when a caller doesn't specify one, picked from the accent's meaning.</summary>
        private static IconUsage defaultIcon(Color4 accent)
        {
            if (accent.Equals(EditorTheme.Colours.Success)) return FontAwesome.Solid.Check;
            if (accent.Equals(EditorTheme.Colours.Error)) return FontAwesome.Solid.ExclamationCircle;
            if (accent.Equals(EditorTheme.Colours.Warning)) return FontAwesome.Solid.ExclamationTriangle;
            return FontAwesome.Solid.InfoCircle;
        }

        private partial class Toast : CompositeDrawable
        {
            public string Message { get; }

            /// <summary>Raised once the toast has begun dismissing (so the overlay can drop its "newest" reference).</summary>
            public event Action? Dismissed;

            private const double hold_ms = 2400;

            private readonly Color4 accent;
            private readonly IconUsage icon;

            private SpriteText countText = null!;
            private int count = 1;
            private ScheduledDelegate? dismiss;
            private bool dismissing;

            public Toast(string message, Color4 accent, IconUsage icon)
            {
                Message = message;
                this.accent = accent;
                this.icon = icon;

                Anchor = Anchor.TopCentre;
                Origin = Anchor.TopCentre;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Lg,
                    BorderThickness = 1f,
                    BorderColour = accent.Opacity(0.32f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = Color4.Black.Opacity(0.35f),
                        Radius = 14,
                        Offset = new Vector2(0, 3),
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = EditorTheme.Colours.Surface,
                            Alpha = 0.98f,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(EditorTheme.Spacing.Sm + 2, 0),
                            Padding = new MarginPadding { Left = EditorTheme.Spacing.Sm + 2, Right = EditorTheme.Spacing.Lg, Vertical = EditorTheme.Spacing.Sm + 1 },
                            Children = new Drawable[]
                            {
                                // Round accent badge with the action's icon - replaces the old generic colour dot.
                                new CircularContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(24),
                                    Masking = true,
                                    Children = new Drawable[]
                                    {
                                        new Box { RelativeSizeAxes = Axes.Both, Colour = accent.Opacity(0.18f) },
                                        new SpriteIcon
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Icon = icon,
                                            Size = new Vector2(12),
                                            Colour = accent,
                                        },
                                    },
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = Message,
                                    Colour = EditorTheme.Colours.Text,
                                    Font = EditorTheme.Type.BodyStrong(),
                                },
                                // "xN" repeat counter, hidden until the same toast is bumped at least once.
                                countText = new SpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Margin = new MarginPadding { Left = 2 },
                                    Colour = EditorTheme.Colours.TextMuted,
                                    Font = EditorTheme.Type.Caption(numeric: true),
                                    Alpha = 0,
                                },
                            },
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Slide down + fade in, then schedule the auto-dismiss.
                this.FadeInFromZero(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease)
                    .MoveToY(-6).MoveToY(0, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

                scheduleDismiss();
            }

            /// <summary>Re-show of the same message: bump the counter, give a little pulse, and reset the timer.</summary>
            public void Bump()
            {
                if (dismissing)
                    return;

                count++;
                countText.Text = $"x{count}";
                countText.FadeIn(EditorTheme.Motion.Fast);

                this.ScaleTo(1.05f, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease)
                    .Then().ScaleTo(1f, EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);

                scheduleDismiss();
            }

            /// <summary>Dismiss early (used when the stack is over its cap).</summary>
            public void DismissNow()
            {
                if (dismissing)
                    return;
                dismiss?.Cancel();
                fadeOutAndExpire(EditorTheme.Motion.Normal);
            }

            private void scheduleDismiss()
            {
                dismiss?.Cancel();
                dismiss = Scheduler.AddDelayed(() => fadeOutAndExpire(EditorTheme.Motion.Slow), hold_ms);
            }

            private void fadeOutAndExpire(double duration)
            {
                if (dismissing)
                    return;
                dismissing = true;
                Dismissed?.Invoke();
                this.FadeOut(duration, EditorTheme.Motion.Ease).Expire();
            }
        }
    }
}
