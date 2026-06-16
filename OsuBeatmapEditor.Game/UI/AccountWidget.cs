using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Online;
using osuTK;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Top-bar account control. Logged out: an empty circular avatar and a "Log in" button. Logged in: the
    /// user's osu! avatar, their name, and a small "Log out" button. Login is optional, so this is purely
    /// additive — it never blocks using the editor.
    /// </summary>
    public partial class AccountWidget : CompositeDrawable
    {
        private const float avatar_size = 34;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private ToastOverlay? toasts { get; set; }

        private FillFlowContainer content = null!;
        private TextureStore? onlineTextures;

        public AccountWidget()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            // A dedicated store that fetches images straight from URLs (osu! avatar CDN).
            onlineTextures = new TextureStore(host.Renderer, host.CreateTextureLoaderStore(new OnlineStore()));

            InternalChild = content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(EditorTheme.Spacing.Lg, 0),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
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

        private void onLoginFailed(string message) => toasts?.Push(message, EditorTheme.Colours.Error);

        private void rebuild()
        {
            content.Clear();

            var user = auth?.User.Value;
            bool loggingIn = auth?.State.Value == AuthState.LoggingIn;

            content.Add(createAvatar(user?.AvatarUrl));

            if (user != null)
            {
                content.Add(new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = user.Username,
                    Colour = EditorTheme.Colours.Text,
                    Font = EditorTheme.Type.BodyStrong(),
                });

                content.Add(new OsuButton("Log out", OsuColour.Surface)
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(72, 28),
                    FontSize = 13,
                    Action = () => auth?.Logout(),
                });
            }
            else
            {
                content.Add(new OsuButton(loggingIn ? "Signing in..." : "Log in", OsuColour.Pink)
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(96, 30),
                    FontSize = 14,
                    Enabled = { Value = !loggingIn },
                    Action = () => auth?.Login(),
                });
            }
        }

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

            var sprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Alpha = 0,
            };

            var circle = new CircularContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(avatar_size),
                Masking = true,
                BorderColour = EditorTheme.Colours.Border,
                BorderThickness = 1.5f,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorTheme.Colours.Control },
                    placeholder,
                    sprite,
                },
            };

            if (!string.IsNullOrEmpty(avatarUrl))
                loadAvatar(avatarUrl!, sprite, placeholder);

            return circle;
        }

        private void loadAvatar(string url, Sprite sprite, Drawable placeholder)
        {
            var store = onlineTextures;
            if (store == null)
                return;

            Task.Run(() =>
            {
                Texture? texture = store.Get(url);
                if (texture == null)
                    return;

                Schedule(() =>
                {
                    sprite.Texture = texture;
                    sprite.FadeIn(EditorTheme.Motion.Normal, EditorTheme.Motion.Ease);
                    placeholder.FadeOut(EditorTheme.Motion.Fast, EditorTheme.Motion.Ease);
                });
            });
        }

        protected override bool OnClick(ClickEvent e) => true; // keep clicks from falling through to the menu

        protected override void Dispose(bool isDisposing)
        {
            if (auth != null)
                auth.LoginFailed -= onLoginFailed;
            base.Dispose(isDisposing);
        }
    }
}
