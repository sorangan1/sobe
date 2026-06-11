namespace OsuBeatmapEditor.Game.Screens.SongSelect
{
    /// <summary>
    /// A carousel card whose selection highlight can be toggled. Implemented by the set and
    /// difficulty panels so the (virtualised) carousel can re-apply selection state when a card
    /// is materialised on demand.
    /// </summary>
    public interface ICarouselPanel
    {
        /// <summary>Toggles the yellow selection border.</summary>
        void SetSelected(bool value);
    }
}
