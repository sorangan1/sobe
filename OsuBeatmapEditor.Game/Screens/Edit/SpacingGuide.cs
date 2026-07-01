using System;
using System.Collections.Generic;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The result of resolving the Shift-held spacing guide for a placement at a cursor position: where the
    /// object should land (<see cref="Position"/>, snapped to a matching nearby gap when one is close enough) plus
    /// the visual guide segments to draw (a solid line + gap label from each nearby anchor to the placed object).
    /// <see cref="Active"/> is false when the mode isn't engaged (Shift not held, or nothing nearby), in which
    /// case the caller falls back to normal placement snapping.
    /// </summary>
    public readonly struct SpacingGuide
    {
        public bool Active { get; init; }

        /// <summary>Where the object should be placed (osu!pixels): a snapped point, or the raw cursor when nothing matched.</summary>
        public Vector2 Position { get; init; }

        /// <summary>True when the position was locked onto a matching nearby gap (drives the highlight).</summary>
        public bool Snapped { get; init; }

        /// <summary>The guide lines to render, one per nearby anchor object.</summary>
        public IReadOnlyList<GuideSegment> Segments { get; init; }

        /// <summary>Blanket wrap rings to render: the live suggestion near the cursor, and existing near-perfect blankets.</summary>
        public IReadOnlyList<BlanketRing> Rings { get; init; }

        public static SpacingGuide Inactive => new SpacingGuide
        {
            Active = false,
            Segments = Array.Empty<GuideSegment>(),
            Rings = Array.Empty<BlanketRing>(),
        };
    }

    /// <summary>
    /// A blanket wrap ring: the arc circle (<see cref="Centre"/> + <see cref="Radius"/>) an object nests into to
    /// form a blanket. <see cref="Active"/> marks the live suggestion near the cursor (bright); otherwise it's an
    /// existing near-perfect blanket being highlighted (faint).
    /// </summary>
    public readonly record struct BlanketRing(Vector2 Centre, float Radius, bool Active);

    /// <summary>
    /// One guide line between two points (osu!pixels), labelled with the edge-to-edge <see cref="Gap"/> between
    /// them. Reference segments (<see cref="Placement"/> = false) connect nearby existing objects to show their
    /// spacing; the placement segment (<see cref="Placement"/> = true) runs from the nearest object to where the
    /// new object would land, and is <see cref="Highlighted"/> when it snapped to a matching gap.
    /// </summary>
    public readonly record struct GuideSegment(Vector2 From, Vector2 To, float Gap, bool Highlighted, bool Placement);
}
