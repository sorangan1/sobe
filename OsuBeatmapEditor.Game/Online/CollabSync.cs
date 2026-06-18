using System;
using System.Linq;
using System.Threading.Tasks;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Statistics;

namespace OsuBeatmapEditor.Game.Online
{
    /// <summary>
    /// Client orchestration for the collaborator side of collab: downloading ("cloning") a collab into lazer's
    /// realm for the first time, and pulling later revisions onto an already-downloaded difficulty. Network +
    /// realm work, so call from a background thread; results are plain (ok, message) tuples for the UI.
    /// </summary>
    public static class CollabSync
    {
        /// <summary>
        /// Bootstraps a collab the user has been added to but doesn't have locally: pulls the head revision and
        /// its assets, then creates the set in lazer's realm and links it.
        /// </summary>
        public static async Task<(bool ok, string message)> DownloadAsync(string token, CollabSummary collab, CollabSession session)
        {
            if (collab.HeadRevision <= 0)
                return (false, "This collab has no map yet.");

            var rev = await SobeApi.PullRevisionAsync(token, collab.Id, collab.HeadRevision).ConfigureAwait(false);
            if (rev == null || string.IsNullOrEmpty(rev.OsuText))
                return (false, "Couldn't download the map.");

            var assets = await SobeApi.GetAssetsAsync(token, collab.Id).ConfigureAwait(false);
            var audio = assets.FirstOrDefault(a => a.Kind == "audio");
            var background = assets.FirstOrDefault(a => a.Kind == "background");
            if (audio == null)
                return (false, "The collab is missing its audio.");

            byte[]? audioBytes = await SobeApi.DownloadAssetAsync(token, collab.Id, "audio").ConfigureAwait(false);
            if (audioBytes == null)
                return (false, "Couldn't download the audio.");

            byte[]? bgBytes = background == null ? null : await SobeApi.DownloadAssetAsync(token, collab.Id, "background").ConfigureAwait(false);

            string? error = BeatmapRealmCreator.BootstrapCollab(rev.OsuText, audioBytes, audio.Filename, bgBytes, background?.Filename);
            if (error != null)
                return (false, error);

            // The local set's identity matches the .osu's metadata (author = Creator), so derive the link key from it.
            var parsed = OsuFileDecoder.Decode(new System.IO.StringReader(rev.OsuText));
            string key = StatisticsTracker.MapKey(parsed.Artist, parsed.Title, parsed.Creator, parsed.Version);
            session.Link(key, collab.Id, rev.Number);

            await SobeApi.MarkSeenAsync(token, collab.Id, rev.Number).ConfigureAwait(false);
            return (true, "Downloaded. Open it from your maps.");
        }

        /// <summary>
        /// Pulls the head revision onto an already-downloaded difficulty, overwriting its .osu in place. Returns a
        /// message; on success the diff is up to date with the server tip.
        /// </summary>
        public static async Task<(bool ok, string message)> PullAsync(string token, CollabSummary collab, CollabSession session)
        {
            string? key = session.KeyForCollab(collab.Id);
            if (key == null)
                return (false, "Download this collab first.");
            if (collab.HeadRevision <= 0)
                return (false, "Nothing to pull yet.");

            var rev = await SobeApi.PullRevisionAsync(token, collab.Id, collab.HeadRevision).ConfigureAwait(false);
            if (rev == null || string.IsNullOrEmpty(rev.OsuText))
                return (false, "Couldn't download the changes.");

            string[] parts = key.Split('|');
            if (parts.Length < 4)
                return (false, "Local map link is corrupt.");

            // Find the local difficulty this collab maps to (matched by the stable artist|title|author|diff key).
            var set = BeatmapStore.LoadAll().FirstOrDefault(s =>
                s.Artist == parts[0] && s.Title == parts[1] && s.Author == parts[2]
                && s.Difficulties.Any(d => d.DifficultyName == parts[3]));
            var difficulty = set?.Difficulties.FirstOrDefault(d => d.DifficultyName == parts[3]);
            if (set == null || difficulty == null)
                return (false, "Couldn't find the downloaded map locally.");

            string? error = BeatmapRealmWriter.SaveRaw(set, difficulty, rev.OsuText);
            if (error != null)
                return (false, error);

            session.SetBaseRevision(key, rev.Number);
            await SobeApi.MarkSeenAsync(token, collab.Id, rev.Number).ConfigureAwait(false);
            return (true, $"Pulled revision {rev.Number}.");
        }
    }
}
