using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using OsuBeatmapEditor.Game.Beatmaps;

namespace OsuBeatmapEditor.Game.UI
{
    /// <summary>
    /// Plays osu! hitsounds for hit objects, using the bundled default skin samples. Resolves the
    /// normal sample plus any whistle/finish/clap additions against the object's resolved sample banks.
    /// Samples are cached on first use; playback routes through the audio manager's effects mixer, so
    /// the global Effects volume applies automatically.
    /// </summary>
    public class HitsoundPlayer
    {
        private readonly ISampleStore samples;
        private readonly Dictionary<string, ISample?> cache = new Dictionary<string, ISample?>();

        public HitsoundPlayer(ISampleStore samples)
        {
            this.samples = samples;
        }

        /// <summary>Plays the hitsounds for a single object (normal + whistle/finish/clap additions).</summary>
        public void Play(HitObjectModel o)
        {
            play($"{bankName(o.NormalBank)}-hitnormal", o.SampleVolume);

            if ((o.HitSound & 0b0010) != 0) play($"{bankName(o.AdditionBank)}-hitwhistle", o.SampleVolume);
            if ((o.HitSound & 0b0100) != 0) play($"{bankName(o.AdditionBank)}-hitfinish", o.SampleVolume);
            if ((o.HitSound & 0b1000) != 0) play($"{bankName(o.AdditionBank)}-hitclap", o.SampleVolume);
        }

        private void play(string name, float volume)
        {
            ISample? sample = get(name);
            if (sample == null)
                return;

            // Per-object volume (from the .osu hitSample / timing point) on top of the master effects mix.
            var channel = sample.GetChannel();
            channel.Volume.Value = volume;
            channel.Play();
        }

        private ISample? get(string name)
        {
            if (!cache.TryGetValue(name, out ISample? sample))
            {
                sample = samples.Get($"Gameplay/{name}");

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
