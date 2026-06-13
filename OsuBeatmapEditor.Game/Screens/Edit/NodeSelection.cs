using System;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The currently-selected slider part (head / tail edge node, or the body) for per-node hitsound editing
    /// and the two-stage selection flow. Distinct from the object-level <see cref="EditorSelection"/>: a part is
    /// identified by its object id plus the node index (0 = head, <c>Slides</c> = tail, <see cref="BodyNode"/> =
    /// the body). At most one part is selected at a time; selecting an edge node lets the hitsound palette target
    /// just that edge of a slider, and the timeline/playfield render it in red.
    /// </summary>
    public class NodeSelection
    {
        /// <summary>
        /// Sentinel node index for "the slider body" (the whole-path part), as opposed to an edge node.
        /// The body carries no per-node hitsound sample, so edits fall through to the whole-object hitsound.
        /// </summary>
        public const int BodyNode = int.MinValue;

        /// <summary>The selected part as (object id, node index), or null if no part is selected.</summary>
        public (int ObjectId, int NodeIndex)? Selected { get; private set; }

        /// <summary>Raised whenever the selected part changes (including being cleared).</summary>
        public event Action? Changed;

        /// <summary>Selects a slider's body part (the whole path), distinct from its head/tail edge nodes.</summary>
        public void SelectBody(int objectId) => Select(objectId, BodyNode);

        /// <summary>Whether the given object's body part is the current selection.</summary>
        public bool IsBodySelected(int objectId) =>
            Selected is { } s && s.ObjectId == objectId && s.NodeIndex == BodyNode;

        public void Select(int objectId, int nodeIndex)
        {
            if (Selected is { } s && s.ObjectId == objectId && s.NodeIndex == nodeIndex)
                return;

            Selected = (objectId, nodeIndex);
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (Selected == null)
                return;

            Selected = null;
            Changed?.Invoke();
        }
    }
}
