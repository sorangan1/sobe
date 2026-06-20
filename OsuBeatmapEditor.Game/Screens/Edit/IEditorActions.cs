using System.Collections.Generic;
using OsuBeatmapEditor.Game.Beatmaps;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
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

        /// <summary>Records the slider's fixed start time when its head is placed, so scrubbing the timeline mid-build doesn't re-snap/move it.</summary>
        void BeginSliderPlacement();

        /// <summary>Clears the recorded slider start time (build cancelled or committed).</summary>
        void EndSliderPlacement();

        /// <summary>Begins a spinner placement: records its start at the current snapped time (osu!lazer model).</summary>
        void BeginSpinnerPlacement();

        /// <summary>Commits the in-progress spinner, ending it at the current snapped time (min one beat long).</summary>
        void FinishSpinnerPlacement();

        /// <summary>Cancels the in-progress spinner placement without committing.</summary>
        void CancelSpinnerPlacement();

        /// <summary>Replaces an existing slider's control points (moving/adding/deleting/typing), resizing it to the new path.</summary>
        void UpdateSliderAnchors(int id, IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>The exact path a slider would take for the given anchors after tick-snapping - for the live reshape preview.</summary>
        IReadOnlyList<Vector2> SnappedSliderPath(int id, IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>The finalized (type-inferred, tick-snapped) path a slider placed now would take - for the live placement preview.</summary>
        IReadOnlyList<Vector2> PlacementSliderPath(IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>The duration (ms) a slider placed now would have (matching <see cref="PlaceSlider"/>) - for the live placement follow points.</summary>
        double PlacementSliderDuration(IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>Live-previews on the top timeline how long a slider being placed (at the current time) will occupy.</summary>
        void PreviewSliderPlacement(IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>Live-previews on the top timeline how long an existing slider would occupy after a reshape.</summary>
        void PreviewSliderResize(int id, IReadOnlyList<SliderControlPoint> controlPoints);

        /// <summary>Clears any slider length preview from the top timeline.</summary>
        void ClearSliderPreview();

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

        // --- Slider reverse/repeat drag (dragging a slider's tail on the top timeline, like osu!lazer) ---

        /// <summary>Snapshots a slider before its tail is dragged to change its repeat (reverse) count.</summary>
        void BeginSliderRepeatDrag(int id);

        /// <summary>Live-updates the dragged slider's repeat count so its tail reaches the given (raw) time.</summary>
        void DragSliderRepeatTo(double endTime);

        /// <summary>Finalises a slider repeat drag (commits the undo step if the count changed).</summary>
        void EndSliderRepeatDrag();

        // --- Slider velocity drag on the timeline (Shift + dragging the tail: change speed, not reverses) ---

        /// <summary>Snapshots a slider before Shift+dragging its tail changes its velocity (via its duration).</summary>
        void BeginSliderVelocityDrag(int id);

        /// <summary>Live-updates the dragged slider's speed so its tail reaches the given (raw) time, path length fixed.</summary>
        void DragSliderVelocityTo(double endTime);

        /// <summary>Finalises a timeline velocity drag (writes the start + tail-reset green lines, commits undo).</summary>
        void EndSliderVelocityDrag();

        // --- Slider length / velocity drag (dragging a slider's tail end-cap on the playfield, like osu!lazer) ---

        /// <summary>Snapshots a slider before its tail end-cap is dragged, recording the grab point (osu!pixels).</summary>
        void BeginSliderLengthDrag(int id, Vector2 osuCursor);

        /// <summary>
        /// Computes (without committing) the slider's body for the cursor at <paramref name="osuCursor"/>: by
        /// default it changes the slider's length (expected distance, tick-snapped, capped at the drawn curve);
        /// with <paramref name="adjustVelocity"/> (Shift) it keeps the duration fixed and changes the slider's
        /// velocity instead. Returns the previewed path and updates the top-timeline length readout.
        /// </summary>
        IReadOnlyList<Vector2> PreviewSliderLength(Vector2 osuCursor, bool adjustVelocity);

        /// <summary>Finalises a slider length/velocity drag (commits the undo step, and a green SV line if velocity changed).</summary>
        void EndSliderLengthDrag();

        // --- Spinner duration drag (dragging a spinner's tail on the top timeline to change its length) ---

        /// <summary>Snapshots a spinner before its tail is dragged to change its end time / duration.</summary>
        void BeginSpinnerDurationDrag(int id);

        /// <summary>Live-updates the dragged spinner's end time to the given (raw) time (min one beat long).</summary>
        void DragSpinnerEndTo(double endTime);

        /// <summary>Finalises a spinner duration drag (commits the undo step if the duration changed).</summary>
        void EndSpinnerDurationDrag();

        // --- Selection transforms (rotate / scale / flip via the Shift selection box, like osu!lazer) ---

        /// <summary>Snapshots the selection before an interactive rotate/scale gesture begins.</summary>
        void BeginSelectionTransform();

        /// <summary>Rotates the selection (from the begin-snapshot) by the given degrees around its centre.</summary>
        void RotateSelection(float degrees);

        /// <summary>Scales the selection (from the begin-snapshot): a width/height delta in osu!px anchored at <paramref name="reference"/>.</summary>
        void ScaleSelection(Vector2 scaleDelta, Anchor reference);

        /// <summary>Finalises an interactive rotate/scale gesture (commits the undo step if anything changed).</summary>
        void EndSelectionTransform();

        /// <summary>Flips the selection horizontally or vertically over its centre (a single, committed edit).</summary>
        void FlipSelection(bool horizontal);

        /// <summary>The osu!pixel bounding box of the current selection's movable objects, or null if none.</summary>
        RectangleF? SelectionBounds();

        // --- Timing points (osu! stable model: red = uninherited/BPM, green = inherited/SV) ---

        /// <summary>Adds a timing point and returns its new stable id.</summary>
        int AddTimingPoint(TimingPointModel point);

        /// <summary>Replaces the timing point with the matching id (keeping its id), then re-derives timing.</summary>
        void UpdateTimingPoint(TimingPointModel point);

        /// <summary>Deletes the timing point with the given id.</summary>
        void DeleteTimingPoint(int id);

        /// <summary>Replaces several timing points at once (by id) as a single undo step.</summary>
        void UpdateTimingPoints(IReadOnlyList<TimingPointModel> points);

        /// <summary>Deletes several timing points at once (by id) as a single undo step.</summary>
        void DeleteTimingPoints(IReadOnlyCollection<int> ids);

        /// <summary>Seeks the editor playhead to the given time (ms), clamped to the track.</summary>
        void SeekTo(double time);

        // --- Hitsound lanes editor (the expanded Clap/Whistle/Finish grid in the top timeline) ---

        /// <summary>
        /// Sets (or clears) a whistle/finish/clap addition bit on one object (<paramref name="nodeIndex"/> &lt; 0)
        /// or a single slider node. Pass <paramref name="pushUndoStep"/> = false to fold the edit into the
        /// previous undo step (so a paint drag across many cells collapses to one undo). Plays the result.
        /// </summary>
        void SetHitsoundAddition(int objectId, int nodeIndex, int bit, bool on, bool pushUndoStep);

        /// <summary>Sets one object's (or slider node's) sample bank: the addition bank when <paramref name="addition"/>, else the normal bank.</summary>
        void SetHitsoundBank(int objectId, int nodeIndex, bool addition, SampleBank bank);
    }
}
