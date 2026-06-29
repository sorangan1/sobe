using System.Collections.Generic;

namespace OsuBeatmapEditor.Game.Annotations
{
    /// <summary>
    /// One authored modding annotation in the shareable "Review" layer (see <see cref="AnnotationDocument"/>).
    /// A single mutable record covers every kind so the file format and the editor can grow without a type
    /// hierarchy; <see cref="Kind"/> selects which fields are meaningful:
    /// <list type="bullet">
    /// <item><b>note</b> — a floating text comment anchored at (<see cref="Time"/>, <see cref="X"/>,<see cref="Y"/>),
    /// shown on the playfield while the playhead is within <see cref="WindowMs"/> of it.</item>
    /// <item><b>shape</b> — a static line / arrow / freehand path (<see cref="Points"/>), visible across the fixed
    /// range [<see cref="Time"/>, <see cref="EndTime"/>]. (Phase 2.)</item>
    /// <item><b>stroke</b> — a dynamic freehand path drawn progressively over <see cref="DurationMs"/> from
    /// <see cref="Time"/>, to illustrate flow/movement during playback. (Phase 3.)</item>
    /// </list>
    /// Anchoring is purely time + playfield XY (osu!pixels); nothing is bound to hit-object ids.
    /// </summary>
    public class Annotation
    {
        public const string KindNote = "note";
        public const string KindShape = "shape";
        public const string KindStroke = "stroke";

        // Note categories (mirroring osu! modding), each shown with its own icon.
        public const string TypeNote = "note";
        public const string TypePraise = "praise";
        public const string TypeProblem = "problem";
        public const string TypeSuggestion = "suggestion";

        /// <summary>Stable id (GUID string) so edits/merges can target a specific annotation.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>One of <see cref="KindNote"/> / <see cref="KindShape"/> / <see cref="KindStroke"/>.</summary>
        public string Kind { get; set; } = KindNote;

        /// <summary>The note category (<see cref="TypeNote"/>/<see cref="TypePraise"/>/<see cref="TypeProblem"/>/<see cref="TypeSuggestion"/>) - drives its icon.</summary>
        public string Type { get; set; } = TypeNote;

        /// <summary>Anchor time in ms (the note's centre time, or a shape/stroke's start time).</summary>
        public double Time { get; set; }

        /// <summary>Anchor position in osu!pixels (a note's pin; the first point of a shape/stroke).</summary>
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>Note text (the <see cref="KindNote"/> comment).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>How long before/after <see cref="Time"/> a note stays visible (full alpha within ~half of this).</summary>
        public double WindowMs { get; set; } = 1500;

        /// <summary>Display name of whoever authored this annotation (notes are coloured per author).</summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>Hex colour (e.g. "#FF66AB") for this annotation; defaults to the author's colour.</summary>
        public string Color { get; set; } = "#FF66AB";

        /// <summary>Ids of hit objects this note's timestamp refers to (highlighted in the author's colour while shown).</summary>
        public List<int>? Objects { get; set; }

        // --- Shape / stroke fields (unused for notes; kept so the format is forward-compatible) ---

        /// <summary>Path points (osu!pixels) for a shape/stroke, as [x, y] pairs.</summary>
        public List<float[]>? Points { get; set; }

        /// <summary>End time for a static <see cref="KindShape"/> (visible across [Time, EndTime]).</summary>
        public double? EndTime { get; set; }

        /// <summary>Reveal duration for a dynamic <see cref="KindStroke"/> (drawn over this span from Time).</summary>
        public double? DurationMs { get; set; }

        /// <summary>Line thickness (osu!pixels) for shapes/strokes.</summary>
        public float? Thickness { get; set; }

        /// <summary>Whether a <see cref="KindShape"/> line draws an arrow head at its end.</summary>
        public bool Arrow { get; set; }

        /// <summary>A deep copy (lists included), so undo/redo snapshots don't share mutable state with the live note.</summary>
        public Annotation Clone() => new Annotation
        {
            Id = Id,
            Kind = Kind,
            Type = Type,
            Time = Time,
            X = X,
            Y = Y,
            Text = Text,
            WindowMs = WindowMs,
            Author = Author,
            Color = Color,
            Objects = Objects == null ? null : new List<int>(Objects),
            Points = Points == null ? null : Points.ConvertAll(p => (float[])p.Clone()),
            EndTime = EndTime,
            DurationMs = DurationMs,
            Thickness = Thickness,
            Arrow = Arrow,
        };
    }
}
