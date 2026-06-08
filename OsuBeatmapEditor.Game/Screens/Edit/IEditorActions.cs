using System.Collections.Generic;
using OsuBeatmapEditor.Game.Beatmaps;
using osuTK;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// Editing operations the timeline and playfield invoke on the open beatmap. Cached by the editor
    /// so input surfaces stay decoupled from the mutation/rebuild logic (as in osu!lazer's editor).
    /// </summary>
    public interface IEditorActions
    {
        /// <summary>Places a new hit circle at the given osu!pixel position (current snapped time).</summary>
        void PlaceCircle(Vector2 osuPosition);

        /// <summary>Places a new slider through the given control points (head first) at the current snapped time.</summary>
        void PlaceSlider(IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>Replaces an existing slider's control points (moving/adding/deleting/typing), resizing it to the new path.</summary>
        void UpdateSliderAnchors(int id, IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>Deletes all currently-selected objects.</summary>
        void DeleteSelected();

        /// <summary>Deletes a single object by id (e.g. right-click quick delete).</summary>
        void DeleteObject(int id);

        /// <summary>Snapshots the selection before a move drag begins.</summary>
        void BeginMove();

        /// <summary>Moves the selection in time, snapping the grabbed object to the beat grid.</summary>
        void MoveSelectionTime(double rawDeltaMs, int grabbedId);

        /// <summary>Moves the selection in playfield position by a raw osu!pixel offset.</summary>
        void MoveSelectionPosition(Vector2 rawDelta);

        /// <summary>Finalises a move drag (re-sorts and flags the map as edited).</summary>
        void EndMove();
    }
}
