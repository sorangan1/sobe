using osu.Framework.Graphics.Sprites;
using OsuBeatmapEditor.Game.Annotations;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>Maps a Review note's <see cref="Annotation.Type"/> to its icon + short label (shared by the bubble, the editor and the timeline markers).</summary>
    public static class ReviewIcons
    {
        public static IconUsage For(string type) => type switch
        {
            Annotation.TypePraise => FontAwesome.Solid.Heart,
            Annotation.TypeProblem => FontAwesome.Solid.ExclamationCircle,
            Annotation.TypeSuggestion => FontAwesome.Solid.Lightbulb,
            _ => FontAwesome.Regular.StickyNote, // note
        };

        public static string Label(string type) => type switch
        {
            Annotation.TypePraise => "Praise",
            Annotation.TypeProblem => "Problem",
            Annotation.TypeSuggestion => "Suggestion",
            _ => "Note",
        };

        /// <summary>The four note types in selector order.</summary>
        public static readonly string[] AllTypes =
        {
            Annotation.TypeNote, Annotation.TypePraise, Annotation.TypeProblem, Annotation.TypeSuggestion,
        };
    }
}
