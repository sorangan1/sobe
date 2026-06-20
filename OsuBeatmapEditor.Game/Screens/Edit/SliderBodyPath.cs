using osu.Framework.Graphics.Lines;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A slider tube whose cross-section is a direct port of osu!lazer's <c>DefaultDrawableSliderPath</c>:
    /// a white rim on the outer <see cref="BorderPortion"/> of the radius, then a gradient body whose
    /// alpha runs from <see cref="opacity_at_edge"/> (just inside the rim) to <see cref="opacity_at_centre"/>
    /// at the centre - scaled by <see cref="BodyOpacity"/> (our configurable object opacity).
    /// <c>position</c> runs 0 (outer edge) to 1 (centre), matching the framework's path texture.
    /// Shared by <see cref="DrawableHitObject"/> (committed sliders) and the live placement/handle preview so
    /// the slider looks identical while being drawn and after it's placed.
    /// </summary>
    public partial class SliderBodyPath : SmoothPath
    {
        // osu!lazer's SliderBody opacity constants (lazer's DrawableSliderPath.BORDER_PORTION is 0.128;
        // here the rim portion is configurable so it can track the hit-circle outline setting).
        private const float opacity_at_centre = 0.3f;
        private const float opacity_at_edge = 0.8f;

        public Color4 BorderColour = Color4.White;
        public Color4 AccentColour = Color4.White;

        /// <summary>Outer fraction of the radius occupied by the white rim (0 = outer edge, 1 = centre).</summary>
        public float BorderPortion = 0.128f;

        /// <summary>Scales the whole body gradient's alpha (1 = exactly lazer's look).</summary>
        public float BodyOpacity = 1f;

        protected override Color4 ColourAt(float position)
        {
            if (position <= BorderPortion)
                return BorderColour;

            float gradientPortion = 1 - BorderPortion;
            position -= BorderPortion;
            float alpha = (opacity_at_edge - (opacity_at_edge - opacity_at_centre) * position / gradientPortion) * BodyOpacity;
            return new Color4(AccentColour.R, AccentColour.G, AccentColour.B, alpha);
        }
    }
}
