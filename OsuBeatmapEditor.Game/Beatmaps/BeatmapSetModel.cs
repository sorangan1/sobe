using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// A plain, UI-facing snapshot of a beatmap set read from osu!lazer's realm.
    /// Intentionally decoupled from Realm types so the rest of the app never touches the database directly.
    /// </summary>
    public class BeatmapSetModel
    {
        public int OnlineID { get; init; } = -1;
        public string Title { get; init; } = string.Empty;
        public string Artist { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;

        /// <summary>The mapper's osu! user id (for their avatar), or -1 when unknown / not an online user.</summary>
        public int AuthorOnlineId { get; init; } = -1;

        public IReadOnlyList<BeatmapDifficultyModel> Difficulties { get; init; } = new List<BeatmapDifficultyModel>();

        /// <summary>osu!lazer data directory these files live under (for resolving the content-addressable store).</summary>
        public string DataDirectory { get; init; } = string.Empty;

        /// <summary>Maps a stored filename (lower-cased, e.g. the audio file) to its SHA-256 hash in the file store.</summary>
        public IReadOnlyDictionary<string, string> Files { get; init; } = new Dictionary<string, string>();

        /// <summary>Stored files keyed by their <b>original-cased</b> filename → SHA-256 hash; used when re-packing
        /// the set into an <c>.osz</c> on export (case matters for the .osu file references on import).</summary>
        public IReadOnlyDictionary<string, string> OriginalFiles { get; init; } = new Dictionary<string, string>();

        /// <summary>Highest star rating among this set's difficulties (used for sorting/labels).</summary>
        public double MaxStarRating => Difficulties.Count == 0 ? 0 : Difficulties.Max(d => d.StarRating);

        /// <summary>Lower-cased haystack used for fast substring search.</summary>
        public string SearchText { get; init; } = string.Empty;

        /// <summary>When the set was imported into osu!lazer (used for the "Date added" sort).</summary>
        public DateTimeOffset DateAdded { get; init; }

        /// <summary>Most recent local update across the set's difficulties (used for the "Date modified" sort).</summary>
        public DateTimeOffset DateModified { get; init; }

        /// <summary>Background image filename for the set as a whole (the first difficulty's background).</summary>
        public string BackgroundFile => Difficulties.FirstOrDefault()?.BackgroundFile ?? string.Empty;

        /// <summary>Stable identity used to track per-set UI state (e.g. the "new" accent).</summary>
        public string Identity => $"{Artist}|{Title}|{Author}";
    }

    /// <summary>A single difficulty within a <see cref="BeatmapSetModel"/>.</summary>
    public class BeatmapDifficultyModel
    {
        public string DifficultyName { get; init; } = string.Empty;
        public double StarRating { get; init; }
        public string RulesetShortName { get; init; } = string.Empty;

        /// <summary>SHA-256 hash of this difficulty's .osu file; also its key in the file store. Empty for unsaved maps.
        /// Updated in place by <see cref="BeatmapRealmWriter"/> after an in-place save so the open editor's snapshot
        /// stays in sync with the realm and a subsequent save can still locate the difficulty by its current hash.</summary>
        public string OsuFileHash { get; set; } = string.Empty;

        /// <summary>This difficulty's background image filename (from its metadata). Empty if none.</summary>
        public string BackgroundFile { get; init; } = string.Empty;

        /// <summary>This difficulty's audio filename (from its metadata). Empty if none.</summary>
        public string AudioFile { get; init; } = string.Empty;

        /// <summary>Mapper-chosen preview point in milliseconds, or -1 if unset.</summary>
        public int PreviewTime { get; init; } = -1;
    }
}
