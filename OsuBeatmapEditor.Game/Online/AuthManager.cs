using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Online
{
    public enum AuthState
    {
        LoggedOut,
        LoggingIn,
        LoggedIn,
    }

    /// <summary>
    /// App-wide osu! login session. Login is optional — the editor works fully without it — and simply
    /// unlocks online features when present. Cached at the game root so any screen can read the current user.
    ///
    /// The flow keeps the osu! secret on the server: we spin up a localhost loopback listener, open the
    /// browser at the backend's <c>/auth/login</c> (which bounces through osu!), and the backend redirects
    /// back to <c>http://localhost:&lt;port&gt;/callback?token=…</c> with our own session token. The token (and
    /// last-known profile) are persisted so the session survives a restart.
    /// </summary>
    public partial class AuthManager : Drawable
    {
        private const string filename = "sobe-auth.json";

        private readonly Storage? storage;
        private readonly GameHost? host;

        /// <summary>The current user, or null when logged out. Bind for UI.</summary>
        public readonly Bindable<SobeUser?> User = new Bindable<SobeUser?>();

        /// <summary>Login lifecycle state. Bind for UI (e.g. to show a spinner while signing in).</summary>
        public readonly Bindable<AuthState> State = new Bindable<AuthState>(AuthState.LoggedOut);

        /// <summary>Raised on the update thread when a login attempt fails (message is user-facing).</summary>
        public event Action<string>? LoginFailed;

        /// <summary>The current session token, or null when logged out.</summary>
        public string? Token { get; private set; }

        public bool IsLoggedIn => Token != null && User.Value != null;

        public AuthManager(Storage? storage, GameHost? host)
        {
            this.storage = storage;
            this.host = host;
            AlwaysPresent = true;
            load();
        }

        /// <summary>Begins the browser-based osu! login. No-op if a login is already in progress.</summary>
        public void Login()
        {
            if (State.Value == AuthState.LoggingIn)
                return;

            State.Value = AuthState.LoggingIn;
            Task.Run(loginAsync);
        }

        /// <summary>Clears the session locally (does not revoke server-side).</summary>
        public void Logout()
        {
            // Tell the server we're going offline before dropping the token (best-effort; we're still alive,
            // so this reliably lands and the web flips to "offline" immediately).
            ReportPresence("offline", null);

            Token = null;
            User.Value = null;
            State.Value = AuthState.LoggedOut;
            deletePersisted();
        }

        /// <summary>Best-effort push of total mapping time to the server (ignored when logged out).</summary>
        public void SyncMappingSeconds(long totalMappingSeconds)
        {
            string? token = Token;
            if (token == null)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await SobeApi.PushMappingSecondsAsync(token, totalMappingSeconds).ConfigureAwait(false);
                }
                catch
                {
                    // Stats sync is best-effort; never surface a failure.
                }
            });
        }

        /// <summary>Best-effort push of per-map active editing time to the server (ignored when logged out).</summary>
        public void SyncMapTimes(IEnumerable<(string Key, long Seconds)> maps)
        {
            string? token = Token;
            if (token == null)
                return;

            var snapshot = maps.ToArray();

            Task.Run(async () =>
            {
                try
                {
                    await SobeApi.PushMapTimesAsync(token, snapshot).ConfigureAwait(false);
                }
                catch
                {
                    // Map-time sync is best-effort; never surface a failure.
                }
            });
        }

        /// <summary>Best-effort presence heartbeat (ignored when logged out).</summary>
        public void ReportPresence(string state, string? map)
        {
            string? token = Token;
            if (token == null)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await SobeApi.PushPresenceAsync(token, state, map).ConfigureAwait(false);
                }
                catch
                {
                    // Presence is best-effort; never surface a failure.
                }
            });
        }

        private async Task loginAsync()
        {
            HttpListener? listener = null;
            try
            {
                int port = findFreePort();

                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();

                host?.OpenUrlExternally($"{SobeApi.BaseUrl}/auth/login?port={port}");

                // Wait for the backend to redirect the browser back to us (with a generous timeout so the
                // user has time to authorize in osu!).
                var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                string token = context.Request.QueryString["token"] ?? string.Empty;

                writeBrowserResponse(context, success: token.Length > 0);
                listener.Stop();

                if (token.Length == 0)
                    throw new InvalidOperationException("Login was cancelled.");

                var user = await SobeApi.GetMeAsync(token).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("Couldn't fetch your profile.");

                Schedule(() => applySession(token, user));
            }
            catch (Exception ex)
            {
                Schedule(() =>
                {
                    State.Value = AuthState.LoggedOut;
                    LoginFailed?.Invoke(friendlyError(ex));
                });
            }
            finally
            {
                try { listener?.Close(); }
                catch { /* already stopped */ }
            }
        }

        private static string friendlyError(Exception ex) =>
            ex is TimeoutException ? "Login timed out — try again." : ex.Message;

        private void applySession(string token, SobeUser user)
        {
            Token = token;
            User.Value = user;
            State.Value = AuthState.LoggedIn;
            persist(token, user);
        }

        /// <summary>Grabs an ephemeral free loopback port for the OAuth callback listener.</summary>
        private static int findFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static void writeBrowserResponse(HttpListenerContext context, bool success)
        {
            string title = success ? "Login complete" : "Login failed";
            string message = success
                ? "You're signed in. You can close this tab and return to sobe."
                : "Something went wrong. Close this tab and try again from sobe.";

            string html =
                "<!doctype html><html><head><meta charset='utf-8'><title>sobe</title></head>" +
                "<body style='font-family:-apple-system,Segoe UI,sans-serif;background:#18181B;color:#ECECEF;" +
                "display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                $"<h2 style='color:#FF66AB'>{title}</h2><p>{message}</p></body></html>";

            byte[] bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }

        // --- Persistence ------------------------------------------------------

        private void load()
        {
            if (storage == null || !storage.Exists(filename))
                return;

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                using var reader = new StreamReader(stream);
                var data = JsonSerializer.Deserialize<PersistedSession>(reader.ReadToEnd());
                if (data?.Token == null || data.User == null)
                    return;

                Token = data.Token;
                User.Value = data.User;
                State.Value = AuthState.LoggedIn;
            }
            catch
            {
                // A corrupt auth file should never break startup; just stay logged out.
            }
        }

        private void persist(string token, SobeUser user)
        {
            if (storage == null)
                return;

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(JsonSerializer.Serialize(new PersistedSession { Token = token, User = user }));
            }
            catch
            {
                // Best-effort; the session simply won't survive a restart.
            }
        }

        private void deletePersisted()
        {
            try
            {
                if (storage != null && storage.Exists(filename))
                    storage.Delete(filename);
            }
            catch
            {
                // Ignore.
            }
        }

        private class PersistedSession
        {
            public string? Token { get; set; }
            public SobeUser? User { get; set; }
        }
    }
}
