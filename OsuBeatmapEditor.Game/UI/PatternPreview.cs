using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using OsuBeatmapEditor.Game.Beatmaps;
using OsuBeatmapEditor.Game.Graphics;
using osuTK;
using osuTK.Graphics;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// A small, non-interactive visual of a saved pattern: its circles drawn as rings and its sliders as thin
    /// paths (reusing each object's computed <see cref="HitObjectModel.Path"/>), scaled to fit the card. Pure
    /// geometry - no timing - so it renders the same regardless of the map it was captured from.
    /// </summary>
    public partial class PatternPreview : CompositeDrawable
    {
        private readonly IReadOnlyList<HitObjectModel> objects;
        private bool laidOut;

        public PatternPreview(IReadOnlyList<HitObjectModel> objects)
        {
            this.objects = objects;
        }

        protected override void Update()
        {
            base.Update();

            // Lay out once we have a real size (DrawSize is 0 at load time).
            if (laidOut || DrawWidth < 2 || DrawHeight < 2)
                return;
            laidOut = true;
            build();
        }

        private void build()
        {
            var points = new List<Vector2>();
            foreach (var o in objects)
            {
                if (o.Kind == HitObjectKind.Spinner)
                    continue; // spinners are full-field; they'd swamp the bounds
                points.Add(new Vector2(o.X, o.Y));
                if (o.Path is { Count: > 0 } path)
                    points.AddRange(path);
            }

            if (points.Count == 0)
            {
                // Spinner-only (or empty) pattern: a single centred ring as a placeholder.
                AddInternal(ring(new Vector2(DrawWidth, DrawHeight) / 2f, System.Math.Min(DrawWidth, DrawHeight) * 0.3f));
                return;
            }

            float minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
            float minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            var boundsMin = new Vector2(minX, minY);
            float spanX = System.Math.Max(1f, maxX - minX);
            float spanY = System.Math.Max(1f, maxY - minY);

            const float pad = 0.86f;
            float scale = System.Math.Min(DrawWidth / spanX, DrawHeight / spanY) * pad;
            // Centre the scaled pattern in the box.
            var drawn = new Vector2(spanX, spanY) * scale;
            var origin = (new Vector2(DrawWidth, DrawHeight) - drawn) / 2f;

            Vector2 map(Vector2 osu) => (osu - boundsMin) * scale + origin;

            // A nominal hit-circle radius (~32 osu!px), clamped so it reads at any scale.
            float circleRadius = System.Math.Clamp(32f * scale, 2.5f, 14f);

            foreach (var o in objects)
            {
                if (o.Kind == HitObjectKind.Slider && o.Path is { Count: > 1 } path)
                {
                    var verts = path.Select(map).ToList();
                    var body = new SmoothPath
                    {
                        PathRadius = System.Math.Max(1.5f, circleRadius * 0.75f),
                        Colour = new Color4(EditorTheme.Colours.Accent.R, EditorTheme.Colours.Accent.G, EditorTheme.Colours.Accent.B, 0.35f),
                        Vertices = verts,
                    };
                    body.Position = -body.PositionInBoundingBox(Vector2.Zero);
                    AddInternal(body);
                }

                if (o.Kind == HitObjectKind.Spinner)
                    continue;

                AddInternal(ring(map(new Vector2(o.X, o.Y)), circleRadius));
            }
        }

        private static Drawable ring(Vector2 centre, float radius) => new CircularContainer
        {
            Position = centre,
            Origin = Anchor.Centre,
            Size = new Vector2(radius * 2f),
            Masking = true,
            BorderThickness = System.Math.Max(1f, radius * 0.28f),
            BorderColour = EditorTheme.Colours.Text,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
        };
    }
}
