using System;
using System.Collections.Generic;
using System.Linq;

namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>How the beatmap carousel is ordered.</summary>
    public enum SortMode
    {
        Artist,
        Title,
        Author,
        Difficulty,
        DateAdded,
        DateModified,
    }

    public static class SortModeExtensions
    {
        public static IEnumerable<Beatmaps.BeatmapSetModel> ApplySort(this IEnumerable<Beatmaps.BeatmapSetModel> sets, SortMode mode)
        {
            return mode switch
            {
                SortMode.Title => sets.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase),
                SortMode.Author => sets.OrderBy(s => s.Author, StringComparer.OrdinalIgnoreCase),
                SortMode.Difficulty => sets.OrderByDescending(s => s.MaxStarRating),
                // Newest first, matching osu!lazer's date sorts.
                SortMode.DateAdded => sets.OrderByDescending(s => s.DateAdded),
                SortMode.DateModified => sets.OrderByDescending(s => s.DateModified),
                _ => sets.OrderBy(s => s.Artist, StringComparer.OrdinalIgnoreCase),
            };
        }
    }
}
