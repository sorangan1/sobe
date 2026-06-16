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
    }
}
