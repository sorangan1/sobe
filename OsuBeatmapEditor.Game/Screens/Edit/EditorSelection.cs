using System;
using System.Collections.Generic;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The editor's shared hit-object selection, keyed by index into the beatmap. Cached so every
    /// surface (top timeline, playfield) reads and writes the same selection - mirroring how osu!lazer
    /// shares one selection between its timeline and composer. Raises <see cref="Changed"/> on any edit.
    /// </summary>
    public class EditorSelection
    {
        private readonly HashSet<int> selected = new HashSet<int>();

        /// <summary>The currently-selected object indices.</summary>
        public IReadOnlyCollection<int> Selected => selected;

        /// <summary>Fired whenever the selection changes.</summary>
        public event Action? Changed;

        public bool Contains(int index) => selected.Contains(index);

        public void Clear()
        {
            if (selected.Count == 0)
                return;

            selected.Clear();
            Changed?.Invoke();
        }

        /// <summary>Removes a single id from the selection if present.</summary>
        public void Deselect(int index)
        {
            if (selected.Remove(index))
                Changed?.Invoke();
        }

        /// <summary>Toggles an index; returns true if it is now selected.</summary>
        public bool Toggle(int index)
        {
            bool nowSelected;
            if (!selected.Remove(index))
            {
                selected.Add(index);
                nowSelected = true;
            }
            else
            {
                nowSelected = false;
            }

            Changed?.Invoke();
            return nowSelected;
        }

        /// <summary>Replaces the selection with a single index.</summary>
        public void SetSingle(int index)
        {
            selected.Clear();
            selected.Add(index);
            Changed?.Invoke();
        }

        /// <summary>Replaces the selection with the given indices.</summary>
        public void SetRange(IEnumerable<int> indices)
        {
            var next = new HashSet<int>(indices);
            if (next.SetEquals(selected))
                return; // no change - avoid firing Changed (and rebuilding visuals) every frame during a drag

            selected.Clear();
            foreach (int i in next)
                selected.Add(i);
            Changed?.Invoke();
        }
    }
}
