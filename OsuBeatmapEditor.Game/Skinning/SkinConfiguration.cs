using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using osu.Framework.Graphics;

namespace OsuBeatmapEditor.Game.Skinning
{
    /// <summary>
    /// The subset of an osu! <c>skin.ini</c> the editor cares about: the bits that change how hit objects are
    /// drawn (combo-number font prefix/overlap, slider colours, slider-ball tinting). Anything we don't read is
    /// ignored; anything missing falls back to a sensible osu!-stable default so an incomplete skin still works.
    /// </summary>
    public class SkinConfiguration
    {
        /// <summary>Skin display name (from <c>[General] Name</c>); falls back to the folder name.</summary>
        public string Name = string.Empty;

        /// <summary>Filename prefix for the combo-number digit textures (<c>[Fonts] HitCirclePrefix</c>, e.g. <c>default-3</c>).</summary>
        public string HitCirclePrefix = "default";

        /// <summary>Pixels of overlap between adjacent combo-number digits (<c>[Fonts] HitCircleOverlap</c>).</summary>
        public int HitCircleOverlap = 2;

        /// <summary>Whether <c>hitcircleoverlay</c> is drawn above the combo number (<c>[General] HitCircleOverlayAboveNumber</c>).</summary>
        public bool HitCircleOverlayAboveNumber = true;

        /// <summary>Whether the slider ball is tinted by the combo colour (<c>[General] AllowSliderBallTint</c>).</summary>
        public bool AllowSliderBallTint;

        /// <summary>Slider-body border colour override (<c>[Colours] SliderBorder</c>); null = use the editor default.</summary>
        public Colour4? SliderBorder;

        /// <summary>Slider-track fill override (<c>[Colours] SliderTrackOverride</c>); null = tint by combo colour.</summary>
        public Colour4? SliderTrackOverride;

        /// <summary>Explicit slider-ball colour (<c>[Colours] SliderBall</c>); null = white / combo-tinted.</summary>
        public Colour4? SliderBall;

        /// <summary>
        /// Parses the key/value pairs we need out of a <c>skin.ini</c> stream. Tolerant of the format's quirks:
        /// case-insensitive keys, <c>//</c> comments, missing sections, <c>Key:Value</c> with or without spaces.
        /// Never throws on malformed input - unparseable lines are skipped.
        /// </summary>
        public static SkinConfiguration Parse(Stream? stream)
        {
            var config = new SkinConfiguration();
            if (stream == null)
                return config;

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = stripComment(line).Trim();
                if (line.Length == 0 || line.StartsWith('[') || !line.Contains(':'))
                    continue;

                int sep = line.IndexOf(':');
                string key = line[..sep].Trim();
                string value = line[(sep + 1)..].Trim();

                switch (key.ToLowerInvariant())
                {
                    case "name":
                        config.Name = value;
                        break;

                    case "hitcircleprefix":
                        if (value.Length > 0)
                            config.HitCirclePrefix = value;
                        break;

                    case "hitcircleoverlap":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlap))
                            config.HitCircleOverlap = overlap;
                        break;

                    case "hitcircleoverlayabovenumber":
                    // osu!stable also accepts this long-standing misspelling.
                    case "hitcircleoverlayabovenumer":
                        config.HitCircleOverlayAboveNumber = value != "0";
                        break;

                    case "allowsliderballtint":
                        config.AllowSliderBallTint = value == "1";
                        break;

                    case "sliderborder":
                        config.SliderBorder = parseColour(value);
                        break;

                    case "slidertrackoverride":
                        config.SliderTrackOverride = parseColour(value);
                        break;

                    case "sliderball":
                        config.SliderBall = parseColour(value);
                        break;
                }
            }

            return config;
        }

        private static string stripComment(string line)
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? line[..c] : line;
        }

        /// <summary>Parses an osu! "r,g,b" (optionally "r,g,b,a", 0-255) colour; null on any malformed value.</summary>
        private static Colour4? parseColour(string value)
        {
            string[] parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return null;

            if (!byte.TryParse(parts[0], out byte r) || !byte.TryParse(parts[1], out byte g) || !byte.TryParse(parts[2], out byte b))
                return null;

            byte a = 255;
            if (parts.Length >= 4)
                byte.TryParse(parts[3], out a);

            return new Colour4(r, g, b, a);
        }
    }
}
