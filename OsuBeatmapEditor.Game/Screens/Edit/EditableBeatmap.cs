using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.Screens.Edit
{
    /// <summary>
    /// The editable metadata and difficulty state of the open beatmap. Bound to the Song Settings UI;
    /// any change flags <see cref="IsDirty"/> so the editor can prompt to save on exit.
    /// </summary>
    public class EditableBeatmap
    {
        public readonly Bindable<string> Title;
        public readonly Bindable<string> TitleUnicode;
        public readonly Bindable<string> Artist;
        public readonly Bindable<string> ArtistUnicode;
        public readonly Bindable<string> Creator;
        public readonly Bindable<string> Version;
        public readonly Bindable<string> Source;
        public readonly Bindable<string> Tags;

        public readonly BindableFloat Hp;
        public readonly BindableFloat Cs;
        public readonly BindableFloat Ar;
        public readonly BindableFloat Od;

        /// <summary>Stack leniency (0-1), how aggressively nearby objects stack.</summary>
        public readonly BindableFloat StackLeniency;

        /// <summary>Base slider velocity multiplier ([Difficulty] SliderMultiplier).</summary>
        public readonly BindableFloat SliderMultiplier;

        /// <summary>Slider tick rate ([Difficulty] SliderTickRate), ticks per beat.</summary>
        public readonly BindableFloat SliderTickRate;

        /// <summary>
        /// The map's own combo colours (its <c>[Colours]</c>), editable in Song Setup and saved to the .osu.
        /// Each entry is its own bindable so a colour swatch can two-way bind to it.
        /// </summary>
        public readonly BindableList<Bindable<Colour4>> MapColours = new BindableList<Bindable<Colour4>>();

        /// <summary>Fired whenever the rendered combo palette changes (map colours, editor palette, or the toggle).</summary>
        public event Action? ColoursChanged;

        public readonly BindableBool IsDirty = new BindableBool();

        private readonly EditorSettings settings;

        // osu! stable default skin combo colours, used when "use map colours" is on but the map has none.
        private static readonly Colour4[] default_skin_colours =
        {
            new Colour4(255, 192, 0, 255),
            new Colour4(0, 202, 0, 255),
            new Colour4(18, 124, 255, 255),
            new Colour4(242, 24, 57, 255),
        };

        public EditableBeatmap(ParsedBeatmap p, string defaultCreator, EditorSettings settings)
        {
            this.settings = settings;

            Title = new Bindable<string>(p.Title);
            TitleUnicode = new Bindable<string>(string.IsNullOrEmpty(p.TitleUnicode) ? p.Title : p.TitleUnicode);
            Artist = new Bindable<string>(p.Artist);
            ArtistUnicode = new Bindable<string>(string.IsNullOrEmpty(p.ArtistUnicode) ? p.Artist : p.ArtistUnicode);
            Creator = new Bindable<string>(string.IsNullOrEmpty(p.Creator) ? defaultCreator : p.Creator);
            Version = new Bindable<string>(p.Version);
            Source = new Bindable<string>(p.Source);
            Tags = new Bindable<string>(p.Tags);

            Hp = difficulty(p.HpDrainRate);
            Cs = difficulty(p.CircleSize);
            Ar = difficulty(p.EffectiveApproachRate);
            Od = difficulty(p.OverallDifficulty);
            StackLeniency = new BindableFloat(p.StackLeniency) { MinValue = 0f, MaxValue = 1f, Precision = 0.1f };
            SliderMultiplier = new BindableFloat(p.SliderMultiplier) { MinValue = 0.4f, MaxValue = 3.6f, Precision = 0.1f };
            SliderTickRate = new BindableFloat(p.SliderTickRate) { MinValue = 1f, MaxValue = 4f, Precision = 1f };

            foreach (var c in p.ComboColours)
                MapColours.Add(makeColour(c));

            void markDirty() => IsDirty.Value = true;

            Title.ValueChanged += _ => markDirty();
            TitleUnicode.ValueChanged += _ => markDirty();
            Artist.ValueChanged += _ => markDirty();
            ArtistUnicode.ValueChanged += _ => markDirty();
            Creator.ValueChanged += _ => markDirty();
            Version.ValueChanged += _ => markDirty();
            Source.ValueChanged += _ => markDirty();
            Tags.ValueChanged += _ => markDirty();
            Hp.ValueChanged += _ => markDirty();
            Cs.ValueChanged += _ => markDirty();
            Ar.ValueChanged += _ => markDirty();
            Od.ValueChanged += _ => markDirty();
            StackLeniency.ValueChanged += _ => markDirty();
            SliderMultiplier.ValueChanged += _ => markDirty();
            SliderTickRate.ValueChanged += _ => markDirty();

            // Adding/removing a map colour is a map edit and changes the rendered palette.
            MapColours.CollectionChanged += (_, _) =>
            {
                markDirty();
                ColoursChanged?.Invoke();
            };

            // The editor palette and the toggle also change what's rendered (but aren't map edits, so no dirty).
            foreach (var b in settings.ComboColours)
                b.ValueChanged += _ => ColoursChanged?.Invoke();
            settings.UseMapColours.ValueChanged += _ => ColoursChanged?.Invoke();
        }

        /// <summary>Appends a new map combo colour (used by the Song Setup colours editor).</summary>
        public void AddMapColour(Colour4 colour) => MapColours.Add(makeColour(colour));

        /// <summary>Removes the given map combo colour entry.</summary>
        public void RemoveMapColour(Bindable<Colour4> colour) => MapColours.Remove(colour);

        /// <summary>
        /// The combo colour for the given combo index, honouring the editor's "use map colours" toggle:
        /// the map's <c>[Colours]</c> (or the default skin palette when it has none) when on, otherwise the
        /// editor's custom palette. Wraps around the chosen palette.
        /// </summary>
        public Colour4 ComboColourFor(int comboIndex)
        {
            if (settings.UseMapColours.Value)
            {
                var palette = MapColours.Count > 0
                    ? MapColours.Select(b => b.Value).ToList()
                    : (IReadOnlyList<Colour4>)default_skin_colours;

                int i = ((comboIndex % palette.Count) + palette.Count) % palette.Count;
                return palette[i];
            }

            return settings.ComboColourFor(comboIndex);
        }

        private Bindable<Colour4> makeColour(Colour4 value)
        {
            var b = new Bindable<Colour4>(value);
            // Editing an existing colour both dirties the map and changes the rendered palette.
            b.ValueChanged += _ =>
            {
                IsDirty.Value = true;
                ColoursChanged?.Invoke();
            };
            return b;
        }

        private static BindableFloat difficulty(float value) => new BindableFloat(value)
        {
            MinValue = 0,
            MaxValue = 10,
            Precision = 0.1f,
        };
    }
}
