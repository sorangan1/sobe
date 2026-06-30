#nullable enable
using System;
using DiscordRPC;
using DiscordRPC.Logging;
using DiscordRPC.Message;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game;
using OsuBeatmapEditor.Game.Screens.Edit;
using OsuBeatmapEditor.Game.Statistics;

namespace OsuBeatmapEditor.Desktop
{
    /// <summary>
    /// Publishes the user's current activity to their Discord profile via the local Discord client (IPC).
    /// Desktop-only — the Discord IPC pipe doesn't exist on mobile, which is why this lives here and not in
    /// the shared Game project. Reads the same <see cref="StatisticsTracker"/> the backend presence uses, so
    /// "editing &lt;map&gt;" vs "browsing" stays consistent. No-op (never even connects) when the user turns the
    /// setting off, or when no Discord client is running. No visual footprint.
    /// </summary>
    public partial class DiscordRichPresence : Drawable
    {
        /// <summary>
        /// The Discord Application ID this presence is published under (from discord.com/developers/applications).
        /// Determines the app name shown ("sobe") and which uploaded art assets are available.
        /// </summary>
        private const string client_id = "1521481868464226356";

        /// <summary>Asset key of the large image uploaded under Rich Presence → Art Assets in the Discord portal.</summary>
        private const string large_image_key = "logo";

        [Resolved(CanBeNull = true)]
        private StatisticsTracker? statistics { get; set; }

        [Resolved(CanBeNull = true)]
        private EditorSettings? settings { get; set; }

        private DiscordRpcClient? client;

        // What we last pushed, so we only call SetPresence when something actually changed.
        private string? lastState;
        private string? lastDetails;

        // When the current "details" line started, so Discord can show the elapsed timer. Reset whenever the
        // map (or the browsing/editing context) changes.
        private DateTime contextStartUtc = DateTime.UtcNow;

        public DiscordRichPresence()
        {
            AlwaysPresent = true;
        }

        private bool isConfigured => client_id != "REPLACE_WITH_DISCORD_APPLICATION_ID" && client_id.Length > 0;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (!isConfigured)
                return;

            client = new DiscordRpcClient(client_id) { Logger = new NullLogger() };

            // If Discord later opens (or the user logs in), re-publish whatever we have instead of waiting for
            // the next change.
            client.OnReady += onReady;

            client.Initialize();

            // React immediately when the user flips the setting, rather than waiting for the next activity change.
            settings?.DiscordRichPresence.BindValueChanged(_ => { lastState = null; lastDetails = null; }, true);
        }

        private void onReady(object? sender, ReadyMessage args) => Schedule(() =>
        {
            lastState = null;
            lastDetails = null;
        });

        protected override void Update()
        {
            base.Update();

            if (client == null || !client.IsInitialized)
                return;

            // Honour the user toggle: clear any shown presence and stop publishing while it's off.
            if (settings?.DiscordRichPresence.Value == false)
            {
                if (lastState != null || lastDetails != null)
                {
                    client.ClearPresence();
                    lastState = lastDetails = null;
                }

                return;
            }

            string? map = statistics?.CurrentMapDisplay;
            bool editing = statistics?.IsActivelyEditing == true;

            // "details" is the headline line (the map, or what you're doing); "state" the secondary line.
            string details;
            string state;

            if (map != null)
            {
                details = map;
                state = editing ? "Editing" : "Idle";
            }
            else
            {
                details = "In song select";
                state = "Browsing";
            }

            if (state == lastState && details == lastDetails)
                return;

            // The context changed — restart the elapsed timer so it reflects the new activity.
            contextStartUtc = DateTime.UtcNow;
            lastState = state;
            lastDetails = details;

            client.SetPresence(new RichPresence
            {
                Details = truncate(details),
                State = truncate(state),
                Timestamps = new Timestamps { Start = contextStartUtc },
                Assets = new Assets
                {
                    LargeImageKey = large_image_key,
                    LargeImageText = $"{AppInfo.Name} {AppInfo.Version}",
                },
            });
        }

        /// <summary>Discord rejects presence strings longer than 128 bytes; keep them comfortably under.</summary>
        private static string truncate(string value) => value.Length <= 128 ? value : value[..127] + "…";

        protected override void Dispose(bool isDisposing)
        {
            if (client != null)
            {
                client.OnReady -= onReady;
                if (client.IsInitialized)
                    client.ClearPresence();
                client.Dispose();
                client = null;
            }

            base.Dispose(isDisposing);
        }
    }
}
