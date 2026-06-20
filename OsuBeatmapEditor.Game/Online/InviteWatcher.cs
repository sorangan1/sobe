using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.UI;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Polls the backend for pending collab invites while logged in and pops a toast for each newly seen one
    /// ("X invited you to collab Y"). The user is not a real member until they accept (from the Collabs list),
    /// so this is purely a notification. No-op when logged out; no visual footprint.
    /// </summary>
    public partial class InviteWatcher : Drawable
    {
        /// <summary>How often to check for new invites. Invites aren't time-critical, so this is relaxed.</summary>
        private const double interval_ms = 60_000;

        [Resolved(CanBeNull = true)]
        private AuthManager? auth { get; set; }

        [Resolved(CanBeNull = true)]
        private ToastOverlay? toasts { get; set; }

        // Invite ids we've already toasted this session, so a repeated poll doesn't re-notify.
        private readonly HashSet<Guid> notified = new HashSet<Guid>();
        private double sincePoll;
        private bool polling;
        private long? lastUserId;

        public InviteWatcher()
        {
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // On (re)login, poll promptly; switching accounts clears the notified set so the new user is told
            // about their own invites.
            if (auth != null)
            {
                auth.User.BindValueChanged(u =>
                {
                    long? id = u.NewValue?.Id;
                    if (id != lastUserId)
                    {
                        notified.Clear();
                        lastUserId = id;
                        sincePoll = interval_ms; // force a check next frame
                    }
                });
            }
        }

        protected override void Update()
        {
            base.Update();

            if (auth == null || !auth.IsLoggedIn || polling)
                return;

            sincePoll += Clock.ElapsedFrameTime;
            if (sincePoll < interval_ms)
                return;

            sincePoll = 0;
            poll();
        }

        private void poll()
        {
            string? token = auth?.Token;
            if (token == null)
                return;

            polling = true;
            Task.Run(async () =>
            {
                var invites = await SobeApi.GetInvitesAsync(token).ConfigureAwait(false);
                Schedule(() =>
                {
                    polling = false;
                    foreach (var invite in invites)
                    {
                        if (!notified.Add(invite.Id))
                            continue;

                        string who = invite.OwnerUsername ?? "Someone";
                        string title = string.IsNullOrEmpty(invite.Title) ? "a map" : invite.Title;
                        toasts?.Push($"{who} invited you to collab \"{title}\" - open Collabs to accept", EditorTheme.Colours.Accent);
                    }
                });
            });
        }
    }
}
