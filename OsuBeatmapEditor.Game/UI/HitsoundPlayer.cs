using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Plays osu! hitsounds for hit objects. Resolves each sample the way osu!lazer does: by
    /// <c>{bank}-{name}{index}</c> (the custom sample-bank index becomes a numeric suffix for index ≥ 2),
    /// trying the <b>beatmap's own samples first</b> and falling back to the bundled default skin. A per-object
    /// custom sample <b>filename</b> (the hitSample's last field) overrides the normal sample outright.
    /// Samples are cached on first use; playback routes through the audio manager's effects mixer, so the
    /// global Effects volume applies automatically.
    /// </summary>
    public class HitsoundPlayer
    {
        private readonly ISampleStore skin;
        private readonly ISampleStore? beatmap;
        private readonly Dictionary<string, ISample?> cache = new Dictionary<string, ISample?>();

        /// <param name="skin">The bundled default-skin sample store (always present, the fallback).</param>
        /// <param name="beatmap">The current map's own sample store (its packed .wav/.mp3/.ogg), consulted first; may be null.</param>
        public HitsoundPlayer(ISampleStore skin, ISampleStore? beatmap = null)
        {
            this.skin = skin;
            this.beatmap = beatmap;
        }

        /// <summary>Plays the hitsounds for a single object's head (normal + whistle/finish/clap additions).</summary>
        public void Play(HitObjectModel o) =>
            Play(o.HitSound, o.NormalBank, o.AdditionBank, o.SampleVolume, o.SampleIndex, o.SampleFilename);

        /// <summary>
        /// Plays one hitsound event: the normal sample always (or the custom <paramref name="filename"/> if set),
        /// plus whistle/finish/clap as flagged in <paramref name="hitSound"/>. The custom sample-bank
        /// <paramref name="index"/> selects suffixed samples (e.g. <c>soft-hitclap2</c>). Used for object heads
        /// and for each slider node (head/repeat/tail).
        /// </summary>
        public void Play(int hitSound, SampleBank normalBank, SampleBank additionBank, float volume, int index = 0, string filename = "")
        {
            // A custom file sample replaces the object's normal hitsound; the bank additions still play on top.
            if (!string.IsNullOrEmpty(filename))
                playLookup(filename, volume);
            else
                playSample(normalBank, "hitnormal", index, volume);

            if ((hitSound & 0b0010) != 0) playSample(additionBank, "hitwhistle", index, volume);
            if ((hitSound & 0b0100) != 0) playSample(additionBank, "hitfinish", index, volume);
            if ((hitSound & 0b1000) != 0) playSample(additionBank, "hitclap", index, volume);
        }

        /// <summary>Plays one slider-tick sound (osu!'s <c>{bank}-slidertick{index}</c>) for the slider's bank/index.</summary>
        public void PlaySliderTick(SampleBank bank, float volume, int index = 0) => playSample(bank, "slidertick", index, volume);

        /// <summary>
        /// Creates a fresh channel for a continuous body sample (<c>sliderslide</c>/<c>sliderwhistle</c>/<c>spinnerspin</c>),
        /// resolved exactly like the one-shots (<c>{bank}-{name}{index}</c> → <c>{bank}-{name}</c>, beatmap store before skin).
        /// The caller owns the channel: set <c>Looping</c>/<c>Volume</c>, then Play and Stop it. Null if no such sample exists.
        /// </summary>
        public SampleChannel? GetLoopChannel(SampleBank bank, string name, int index) => resolveSample(bank, name, index)?.GetChannel();

        /// <summary>
        /// Creates a fresh channel for a bank-less loop sample (osu!'s <c>spinnerspin</c>/<c>spinnerbonus</c> carry no
        /// <c>normal-/soft-/drum-</c> prefix). Beatmap store first then skin. Null if the sample is absent.
        /// </summary>
        public SampleChannel? GetLoopChannel(string name) => get(name)?.GetChannel();

        /// <summary>Resolves <c>{bank}-{name}{index}</c> (suffix only for index ≥ 2, falling back to no suffix) and plays it.</summary>
        private void playSample(SampleBank bank, string name, int index, float volume)
        {
            ISample? sample = resolveSample(bank, name, index);
            if (sample != null)
                playChannel(sample, volume);
        }

        /// <summary>Resolves a bank sample by <c>{bank}-{name}{index}</c> (suffix only for index ≥ 2), beatmap store first then skin.</summary>
        private ISample? resolveSample(SampleBank bank, string name, int index)
        {
            string b = bankName(bank);
            // osu! convention: index 0/1 use the un-suffixed sample; index ≥ 2 looks up a numbered variant first.
            if (index >= 2)
            {
                ISample? suffixed = get($"{b}-{name}{index}");
                if (suffixed != null)
                    return suffixed;
            }
            return get($"{b}-{name}");
        }

        /// <summary>Plays a named sample directly (a custom file), beatmap store first then skin; no-op if neither has it.</summary>
        private void playLookup(string name, float volume)
        {
            ISample? sample = get(name);
            if (sample != null)
                playChannel(sample, volume);
        }

        private static void playChannel(ISample sample, float volume)
        {
            // Per-object volume (from the .osu hitSample / timing point) on top of the master effects mix.
            var channel = sample.GetChannel();
            channel.Volume.Value = volume;
            channel.Play();
        }

        private ISample? get(string name)
        {
            if (!cache.TryGetValue(name, out ISample? sample))
            {
                // Beatmap's own samples take priority (looked up by their packed filename), then the default skin
                // (under the "Gameplay/" namespace). Either store appends wav/mp3/ogg extensions itself.
                sample = beatmap?.Get(name) ?? skin.Get($"Gameplay/{name}");

                // The default concurrency (2) drops rapidly-repeated hits, so dense streams fall silent.
                // Allow many overlapping plays of the same sample, like a real osu! gameplay sample pool.
                if (sample != null)
                    sample.PlaybackConcurrency.Value = 32;

                cache[name] = sample;
            }

            return sample;
        }

        private static string bankName(SampleBank bank) => bank switch
        {
            SampleBank.Soft => "soft",
            SampleBank.Drum => "drum",
            _ => "normal",
        };
    }
}
