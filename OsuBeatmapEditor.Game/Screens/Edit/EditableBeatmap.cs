using osu.Framework.Bindables;
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

        public readonly BindableBool IsDirty = new BindableBool();

        public EditableBeatmap(ParsedBeatmap p, string defaultCreator)
        {
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
        }

        private static BindableFloat difficulty(float value) => new BindableFloat(value)
        {
            MinValue = 0,
            MaxValue = 10,
            Precision = 0.1f,
        };
    }
}
