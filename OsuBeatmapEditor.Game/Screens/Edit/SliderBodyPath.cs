using osu.Framework.Graphics.Lines;
using OsuBeatmapEditor.Game.Graphics;
using OsuBeatmapEditor.Game.Skinning;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// A slider tube: a white rim on the outer <see cref="BorderPortion"/> of the radius, then a body that
    /// gradients from <see cref="EdgeColour"/>/<see cref="EdgeOpacity"/> just inside the rim to
    /// <see cref="CentreColour"/>/<see cref="CentreOpacity"/> at the centre (all alpha scaled by
    /// <see cref="BodyOpacity"/>). <c>position</c> runs 0 (outer edge) to 1 (centre), matching the framework's
    /// path texture. Shared by <see cref="DrawableHitObject"/> (committed sliders) and the live placement/handle
    /// preview so the slider looks identical while being drawn and after it's placed.
    /// The body is darker at the edges and brighter toward the centre (like osu!'s slider glow), which is what
    /// makes the gradient read against the dark playfield.
    /// </summary>
    public partial class SliderBodyPath : SmoothPath
    {
        /// <summary>How far the centre colour is lightened toward white from the edge colour (0 = same, 1 = white).</summary>
        private const float centre_lighten = 0.6f;

        public Color4 BorderColour = Color4.White;

        /// <summary>Body colour just inside the rim (the outer edge of the gradient).</summary>
        public Color4 EdgeColour = Color4.White;

        /// <summary>Body colour at the centre of the tube (brighter, so the gradient is visible).</summary>
        public Color4 CentreColour = Color4.White;

        /// <summary>Body alpha just inside the rim (the outer edge of the gradient).</summary>
        public float EdgeOpacity = 0.3f;

        /// <summary>Body alpha at the centre of the tube.</summary>
        public float CentreOpacity = 0.8f;

        /// <summary>Outer fraction of the radius occupied by the white rim (0 = outer edge, 1 = centre).</summary>
        public float BorderPortion = 0.128f;

        /// <summary>Scales the whole body gradient's alpha (1 = exactly the configured edge/centre values).</summary>
        public float BodyOpacity = 1f;

        /// <summary>
        /// Sets the border colour, body gradient (dark edge -> bright centre) and opacities from the active skin,
        /// or the built-in look when <paramref name="skin"/> is null. Centralised so committed sliders and the
        /// placement preview stay in sync. No skin: white rim, a fixed dark-grey edge fading to a light-grey
        /// centre (NOT the combo colour). Skin: its <c>SliderBorder</c>/<c>SliderTrackOverride</c> (track falling
        /// back to the combo colour) with a lighter centre, and a near-solid body since osu! skins render slider
        /// bodies as a solid fill.
        /// </summary>
        public void ApplySkinAppearance(Skin? skin, Color4 comboColour)
        {
            bool skinned = skin != null;

            BorderColour = skin?.Config.SliderBorder is { } sb ? new Color4(sb.R, sb.G, sb.B, sb.A) : Color4.White;

            Color4 track = skin == null
                ? EditorTheme.Colours.SliderBodyDefault
                : skin.Config.SliderTrackOverride is { } st ? new Color4(st.R, st.G, st.B, st.A) : comboColour;

            EdgeColour = track;
            CentreColour = lighten(track, centre_lighten);

            EdgeOpacity = skinned ? 0.9f : 0.3f;
            CentreOpacity = skinned ? 1f : 0.8f;
        }

        protected override Color4 ColourAt(float position)
        {
            if (position <= BorderPortion)
                return BorderColour;

            // t: 0 at the inner edge of the rim, 1 at the centre.
            float t = (position - BorderPortion) / (1 - BorderPortion);

            float r = EdgeColour.R + (CentreColour.R - EdgeColour.R) * t;
            float g = EdgeColour.G + (CentreColour.G - EdgeColour.G) * t;
            float b = EdgeColour.B + (CentreColour.B - EdgeColour.B) * t;
            float alpha = (EdgeOpacity + (CentreOpacity - EdgeOpacity) * t) * BodyOpacity;

            return new Color4(r, g, b, alpha);
        }

        /// <summary>Interpolates a colour toward white by <paramref name="amount"/> (0 = unchanged, 1 = white).</summary>
        private static Color4 lighten(Color4 c, float amount) => new Color4(
            c.R + (1 - c.R) * amount,
            c.G + (1 - c.G) * amount,
            c.B + (1 - c.B) * amount,
            c.A);
    }
}
