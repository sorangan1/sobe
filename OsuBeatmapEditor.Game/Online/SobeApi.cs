using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Thin HTTP client for the sobe backend. The desktop app only ever talks to our own server (never to
    /// osu! directly); the server holds the osu! secret and hands back a session token used here as a bearer.
    /// </summary>
    public static class SobeApi
    {
        /// <summary>Base URL of the deployed backend (Railway).</summary>
        public const string BaseUrl = "https://sobe-server-production.up.railway.app";

        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private static readonly JsonSerializerOptions json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>Fetches the profile for the given session token, or null if the token is invalid.</summary>
        public static async Task<SobeUser?> GetMeAsync(string token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<SobeUser>(body, json);
        }

        /// <summary>Fetches the beatmap discussions ("mods") for an uploaded beatmapset. Public (no token).
        /// Returns an empty list for a non-online map (id &lt;= 0) or on any failure, so Modding Mode never breaks.</summary>
        public static async Task<List<ModdingDiscussion>> GetDiscussionsAsync(int beatmapsetId)
        {
            if (beatmapsetId <= 0)
                return new List<ModdingDiscussion>();

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/beatmapsets/{beatmapsetId}/discussions");
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<ModdingDiscussion>();

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<ModdingDiscussion>>(body, json) ?? new List<ModdingDiscussion>();
            }
            catch
            {
                return new List<ModdingDiscussion>();
            }
        }

        /// <summary>Pushes the user's total active mapping time (seconds). The server keeps the max, so this
        /// can be called freely; a stale value never lowers the recorded total.</summary>
        public static async Task PushMappingSecondsAsync(string token, long totalMappingSeconds)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/stats");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { totalMappingSeconds }), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        /// <summary>Pushes per-map active editing time (seconds, keyed by the stable map key). The server keeps
        /// the max per map, so over-sending is harmless.</summary>
        public static async Task PushMapTimesAsync(string token, IEnumerable<(string Key, long Seconds)> maps)
        {
            var payload = new
            {
                maps = maps.Select(m => new { key = m.Key, seconds = m.Seconds }).ToArray(),
            };
            if (payload.maps.Length == 0)
                return;

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/maps");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        /// <summary>Reports the user's current presence ("online" or "editing" + the map being edited).</summary>
        public static async Task PushPresenceAsync(string token, string state, string? map)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/presence");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { state, map }), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        // ---- Collab ("git for maps") -------------------------------------------------

        private static HttpRequestMessage Authed(HttpMethod method, string path, string token)
        {
            var req = new HttpRequestMessage(method, $"{BaseUrl}{path}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return req;
        }

        private static StringContent JsonBody(object payload) =>
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        /// <summary>Creates a collab for one difficulty; returns its server handle, or null on failure.</summary>
        public static async Task<Guid?> CreateCollabAsync(string token, string title, long? onlineBeatmapsetId)
        {
            using var req = Authed(HttpMethod.Post, "/api/collabs", token);
            req.Content = JsonBody(new { title, onlineBeatmapsetId });

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetGuid(out var id) ? id : (Guid?)null;
        }

        /// <summary>Adds a collaborator by osu! username. False if the user isn't known to sobe or the call fails.</summary>
        public static async Task<bool> AddMemberAsync(string token, Guid collabId, string username)
        {
            using var req = Authed(HttpMethod.Post, $"/api/collabs/{collabId}/members", token);
            req.Content = JsonBody(new { username });

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }

        /// <summary>Lists the collabs the current user belongs to (drives the "added" / "changes available" UI).</summary>
        public static async Task<List<CollabSummary>> GetMyCollabsAsync(string token)
        {
            try
            {
                using var req = Authed(HttpMethod.Get, "/api/collabs/mine", token);
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<CollabSummary>();

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<CollabSummary>>(body, json) ?? new List<CollabSummary>();
            }
            catch
            {
                return new List<CollabSummary>();
            }
        }

        /// <summary>Fetches the current tip metadata of a collab (cheap; for the poll). Null on failure.</summary>
        public static async Task<CollabHead?> GetHeadAsync(string token, Guid collabId)
        {
            try
            {
                using var req = Authed(HttpMethod.Get, $"/api/collabs/{collabId}/head", token);
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return null;

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<CollabHead>(body, json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Downloads a revision's full .osu text (a pull). Null on failure.</summary>
        public static async Task<CollabRevisionContent?> PullRevisionAsync(string token, Guid collabId, int number)
        {
            using var req = Authed(HttpMethod.Get, $"/api/collabs/{collabId}/revisions/{number}", token);
            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<CollabRevisionContent>(body, json);
        }

        /// <summary>Pushes a new revision (fast-forward only). On 409 the result carries the server tip to merge onto.</summary>
        public static async Task<CollabPushResult> PushRevisionAsync(string token, Guid collabId, int baseRevision, string osuText, string? message)
        {
            using var req = Authed(HttpMethod.Post, $"/api/collabs/{collabId}/push", token);
            req.Content = JsonBody(new { baseRevision, osuText, message });

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                int head = 0;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("headRevision", out var h) && h.TryGetInt32(out var hv))
                        head = hv;
                }
                catch { /* leave head = 0 */ }
                return new CollabPushResult(false, 0, true, head, false);
            }

            if (!resp.IsSuccessStatusCode)
                return new CollabPushResult(false, 0, false, 0, false);

            int number = 0;
            bool noOp = false;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv))
                    number = nv;
                if (doc.RootElement.TryGetProperty("noOp", out var no) && no.ValueKind == JsonValueKind.True)
                    noOp = true;
            }
            catch { /* leave defaults */ }

            return new CollabPushResult(true, number, false, number, noOp);
        }

        /// <summary>Marks how far this user has pulled (clears the "unread" badge). Best-effort.</summary>
        public static async Task MarkSeenAsync(string token, Guid collabId, int revision)
        {
            try
            {
                using var req = Authed(HttpMethod.Post, $"/api/collabs/{collabId}/seen", token);
                req.Content = JsonBody(new { revision });
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                _ = resp.IsSuccessStatusCode;
            }
            catch { /* best-effort */ }
        }

        /// <summary>Lists a collab's asset metadata (audio/background) so the client can skip bytes it already has.</summary>
        public static async Task<List<CollabAssetInfo>> GetAssetsAsync(string token, Guid collabId)
        {
            try
            {
                using var req = Authed(HttpMethod.Get, $"/api/collabs/{collabId}/assets", token);
                using var resp = await http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return new List<CollabAssetInfo>();

                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<List<CollabAssetInfo>>(body, json) ?? new List<CollabAssetInfo>();
            }
            catch
            {
                return new List<CollabAssetInfo>();
            }
        }

        /// <summary>Downloads an asset's raw bytes (audio/background) for bootstrapping. Null on failure.</summary>
        public static async Task<byte[]?> DownloadAssetAsync(string token, Guid collabId, string kind)
        {
            using var req = Authed(HttpMethod.Get, $"/api/collabs/{collabId}/assets/{kind}", token);
            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>Uploads/replaces an asset's bytes (last write wins). The filename is what the .osu references.</summary>
        public static async Task<bool> UploadAssetAsync(string token, Guid collabId, string kind, string filename, byte[] data)
        {
            string path = $"/api/collabs/{collabId}/assets/{kind}?filename={Uri.EscapeDataString(filename)}";
            using var req = Authed(HttpMethod.Put, path, token);
            req.Content = new ByteArrayContent(data);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
    }
}
