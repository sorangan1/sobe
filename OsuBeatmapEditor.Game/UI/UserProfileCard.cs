using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using OsuBeatmapEditor.Game.Statistics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Top-bar account card. Flush to the top-right corner (no margin, full bar height) with the logged-in
    /// player's osu! profile header as its background, their square avatar and name on top. Clicking it drops
    /// a panel down over the carousel with the usage-time stats and a logout button. Logged out, it collapses
    /// to a single "Log in" button — login is optional, so this never blocks using the editor.
    /// </summary>
    public partial class UserProfileCard : CompositeDrawable
    {
        /// <summary>Card width (fixed so neighbouring chrome can position itself against it).</summary>
        public const float CardWidth = 250;

        private const float bar_height = TopBar.HeightPx;
        private const float avatar_size = 34;
        private const float dropdown_padding = 14;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        [Resolved(CanBeNull = true)]
        private ToastOverlay? toasts { get; set; }

        private Container collapsed = null!;
        private Container dropdown = null!;
        private SpriteText openValue = null!;
        private SpriteText activeValue = null!;

        private TextureStore? onlineTextures;
        private bool expanded;

        public UserProfileCard()
        {
            Width = CardWidth;
            Height = bar_height;
        }

        [BackgroundDependencyLoader]
        private void load(OnlineTextureStore onlineTextures)
        {
            // Shared, disk-cached store that fetches images straight from URLs (osu! avatar/cover CDN).
            this.onlineTextures = onlineTextures;

            InternalChildren = new Drawable[]
            {
                // The drop-down panel sits below the collapsed card; built first so the card draws above it.
                dropdown = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Y = bar_height,
                    Width = CardWidth,
                    AutoSizeAxes = Axes.Y,
                    Masking = true,
                    CornerRadius = EditorTheme.Radius.Md,
                    Alpha = 0,
                    Scale = new Vector2(1, 0),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Colour = Color4.Black.Opacity(0.45f),
                        Radius = 12,
                        Offset = new Vector2(0, 4),
                    },
                },
                collapsed = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (auth != null)
            {
                auth.User.BindValueChanged(_ => rebuild(), true);
                auth.State.BindValueChanged(_ => rebuild());
                auth.LoginFailed += onLoginFailed;
            }
            else
            {
                rebuild();
            }
        }

        protected override void Update()
        {
            base.Update();

            // Only worth refreshing while the stats panel is on screen.
            if (!expanded || statistics == null)
                return;

            openValue.Text = StatisticsTracker.Format(statistics.TotalOpenMs);
            activeValue.Text = StatisticsTracker.Format(statistics.TotalActiveMs);
        }

        private void onLoginFailed(string message) => toasts?.Push(message, EditorTheme.Colours.Error);

        // --- Building the two states ---------------------------------------------------------------

        private bool built;
        private long? shownUserId;
        private bool shownLoggingIn;

        private void rebuild()
        {
            var user = auth?.User.Value;
            bool loggingIn = auth?.State.Value == AuthState.LoggingIn;

            // On login both User and State fire; rebuilding twice would re-fetch the avatar/cover and
            // restart their fade. Skip when nothing visible has actually changed.
            if (built && user?.Id == shownUserId && (user != null || loggingIn == shownLoggingIn))
                return;

            built = true;
            shownUserId = user?.Id;
            shownLoggingIn = loggingIn;

            setExpanded(false);
            collapsed.Clear();
            dropdown.Clear();

            if (user == null)
            {
                buildLoggedOut(loggingIn);
                return;
            }

            buildCard(user);
            buildDropdown();
        }

        private void buildLoggedOut(bool loggingIn)
        {
            // Mirror the signed-in card layout, but with an empty avatar and "Log In" in place of the
            // username. Clicking anywhere on the card starts the osu! login (handled in OnClick).
            collapsed.Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Horizontal = 12 },
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        createAvatar(null),
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = loggingIn ? "Signing in..." : "Log In",
                            Colour = EditorTheme.Colours.Text,
                            Font = EditorTheme.Type.Heading(),
                        },
                    },
                },
            };
        }

        private void buildCard(SobeUser user)
        {
            var coverLayer = new Container { RelativeSizeAxes = Axes.Both };

            collapsed.Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Raised },
                coverLayer,
                // Darken the cover so the avatar + name stay legible (heavier on the left).
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientHorizontal(
                        Color4.Black.Opacity(0.75f),
                        Color4.Black.Opacity(0.35f)),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Horizontal = 12 },
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        createAvatar(user.AvatarUrl),
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = user.Username,
                            Colour = Color4.White,
                            Font = EditorTheme.Type.Heading(),
                            Truncate = true,
                            // Leave room for the avatar + the chevron.
                            MaxWidth = CardWidth - avatar_size - 60,
                        },
                    },
                },
                new SpriteIcon
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 12 },
                    Icon = FontAwesome.Solid.ChevronDown,
                    Size = new Vector2(11),
                    Colour = Color4.White.Opacity(0.85f),
                },
            };

            loadRemote(user.CoverUrl, coverLayer, null);
        }

        private void buildDropdown()
        {
            dropdown.Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Overlay },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, dropdown_padding),
                        Padding = new MarginPadding(dropdown_padding),
                        Children = new Drawable[]
                        {
                            createStat("EDITOR OPEN", out openValue),
                            createStat("TIME MAPPING", out activeValue),
                            buildLogoutButton(),
                        },
                    },
                },
            };
        }

        private Drawable createStat(string label, out SpriteText value)
        {
            value = new SpriteText
            {
                Text = "--",
                Colour = EditorTheme.Colours.Text,
                Font = EditorTheme.Type.Title(numeric: true),
            };

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = label,
                        Colour = EditorTheme.Colours.TextMuted,
                        Font = EditorTheme.Type.Caption(),
                    },
                    value,
                },
            };
        }

        private Drawable buildLogoutButton()
        {
            var bg = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control };

            return new HoverClickContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = EditorTheme.Sizing.ButtonHeight + 4,
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                HoverColour = EditorTheme.Colours.Error,
                Background = bg,
                Action = () =>
                {
                    setExpanded(false);
                    auth?.Logout();
                },
                Children = new Drawable[]
                {
                    bg,
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.SignOutAlt,
                                Size = new Vector2(14),
                                Colour = EditorTheme.Colours.Text,
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "Log out",
                                Colour = EditorTheme.Colours.Text,
                                Font = EditorTheme.Type.BodyStrong(),
                            },
                        },
                    },
                },
            };
        }

        // --- Avatar / cover loading ----------------------------------------------------------------

        private Drawable createAvatar(string? avatarUrl)
        {
            var placeholder = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.User,
                Size = new Vector2(avatar_size * 0.5f),
                Colour = EditorTheme.Colours.TextFaint,
            };

            // A square avatar with rounded corners (not a circle).
            var box = new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(avatar_size),
                Masking = true,
                CornerRadius = EditorTheme.Radius.Md,
                BorderColour = Color4.White.Opacity(0.25f),
                BorderThickness = 1.5f,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    placeholder,
                },
            };

            loadRemote(avatarUrl, box, placeholder);
            return box;
        }

        /// <summary>
        /// Loads a remote image (avatar/cover) off the update thread and adds it into <paramref name="target"/>
        /// once ready, fading it in over the placeholder. Uses <see cref="CompositeDrawable.LoadComponentAsync{TLoadable}"/>
        /// so the texture is fetched on the framework's load thread (the reliable path; a raw <c>Task</c> + a
        /// manual store access can drop the upload), making it appear as soon as it's fetched on login.
        /// </summary>
        private void loadRemote(string? url, Container target, Drawable? placeholder)
        {
            if (string.IsNullOrEmpty(url) || onlineTextures == null)
                return;

            LoadComponentAsync(new RemoteImage(url!, onlineTextures), img =>
            {
                target.Add(img);
                img.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                placeholder?.FadeOut(EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            });
        }

        // --- Expansion -----------------------------------------------------------------------------

        private void setExpanded(bool value)
        {
            if (expanded == value)
                return;

            expanded = value;

            dropdown.ClearTransforms();
            if (value)
            {
                dropdown.ScaleTo(new Vector2(1), EditorTheme.Motion.Slow, EditorTheme.Motion.Ease)
                        .FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
            }
            else
            {
                dropdown.ScaleTo(new Vector2(1, 0), EditorTheme.Motion.Normal, EditorTheme.Motion.Ease)
                        .FadeOut(EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
            }
        }

        // Take input across the card and (while open) the dropdown, so clicks on the panel's empty
        // space are swallowed rather than reaching the carousel behind it.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
            => base.ReceivePositionalInputAt(screenSpacePos)
               || (expanded && dropdown.ReceivePositionalInputAt(screenSpacePos));

        protected override bool OnClick(ClickEvent e)
        {
            if (auth?.User.Value == null)
            {
                // Logged out: the whole card is the login affordance.
                auth?.Login();
            }
            else if (collapsed.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
            {
                // Signed in: a click on the card toggles the stats dropdown. The dropdown's own controls
                // handle their clicks, and clicks on its empty space are simply absorbed.
                setExpanded(!expanded);
            }

            return true; // keep clicks from falling through to the carousel
        }

        protected override void Dispose(bool isDisposing)
        {
            if (auth != null)
                auth.LoginFailed -= onLoginFailed;
            base.Dispose(isDisposing);
        }

        /// <summary>A rounded clickable surface that tints on hover; used for the logout row.</summary>
        private partial class HoverClickContainer : osu.Framework.Graphics.Containers.ClickableContainer
        {
            public Color4 HoverColour { get; init; }
            public Box Background { get; init; } = null!;

            private ColourInfo idle;

            protected override void LoadComplete()
            {
                base.LoadComplete();
                idle = Background.Colour;
            }

            protected override bool OnHover(HoverEvent e)
            {
                Background.FadeColour(HoverColour, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e) =>
                Background.FadeColour(idle, EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
        }
    }
}
