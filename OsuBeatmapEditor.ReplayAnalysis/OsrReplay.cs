using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace OsuBeatmapEditor.ReplayAnalysis
{
    /// <summary>One cursor sample from a replay: absolute time (ms, map/audio time) and osu!pixel position.</summary>
    public readonly record struct ReplayFrame(double Time, float X, float Y, int Keys);

    /// <summary>
    /// A parsed osu! <c>.osr</c> replay. Only the header fields we need plus the decompressed cursor frames.
    /// Format reference: https://osu.ppy.sh/wiki/en/Client/File_formats/osr_%28file_format%29
    /// The frame block is LZMA-compressed ("alone"/.lzma layout: 5 prop bytes + 8 size bytes + data) and decodes
    /// to comma-separated <c>w|x|y|z</c> tokens (w = ms since previous frame, x/y = osu!pixels, z = key bitmask).
    /// </summary>
    public sealed class OsrReplay
    {
        public byte GameMode { get; init; }
        public string BeatmapMd5 { get; init; } = string.Empty;
        public int Mods { get; init; }
        public IReadOnlyList<ReplayFrame> Frames { get; init; } = Array.Empty<ReplayFrame>();

        public static OsrReplay Parse(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms, Encoding.UTF8);

            byte mode = r.ReadByte();
            r.ReadInt32();                       // game version
            string mapMd5 = ReadOsuString(r);
            ReadOsuString(r);                    // player name
            ReadOsuString(r);                    // replay MD5
            r.ReadInt16(); r.ReadInt16(); r.ReadInt16(); // 300 / 100 / 50
            r.ReadInt16(); r.ReadInt16();        // geki / katu
            r.ReadInt16();                       // miss
            r.ReadInt32();                       // total score
            r.ReadInt16();                       // max combo
            r.ReadByte();                        // perfect
            int mods = r.ReadInt32();
            ReadOsuString(r);                    // life-bar graph
            r.ReadInt64();                       // timestamp (ticks)
            int compressedLength = r.ReadInt32();
            byte[] block = r.ReadBytes(compressedLength);

            return new OsrReplay
            {
                GameMode = mode,
                BeatmapMd5 = mapMd5,
                Mods = mods,
                Frames = parseFrames(decompress(block)),
            };
        }

        private static string decompress(byte[] block)
        {
            if (block.Length < 13)
                return string.Empty;

            byte[] props = new byte[5];
            Array.Copy(block, 0, props, 0, 5);
            long outSize = BitConverter.ToInt64(block, 5); // may be -1 (unknown)

            using var input = new MemoryStream(block, 13, block.Length - 13);
            using var lzma = new LzmaStream(props, input, block.Length - 13, outSize);
            using var output = new MemoryStream();
            lzma.CopyTo(output);
            return Encoding.ASCII.GetString(output.ToArray());
        }

        private static List<ReplayFrame> parseFrames(string text)
        {
            var frames = new List<ReplayFrame>();
            double t = 0;

            foreach (string token in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = token.Split('|');
                if (parts.Length != 4)
                    continue;

                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long w))
                    continue;

                // The trailing RNG-seed frame uses w = -12345; never a real cursor sample.
                if (w == -12345)
                    continue;

                if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                    !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                    continue;

                t += w;
                frames.Add(new ReplayFrame(t, x, y, z));
            }

            frames.Sort((a, b) => a.Time.CompareTo(b.Time));
            return frames;
        }

        /// <summary>osu! string: 0x00 = empty, else 0x0b + ULEB128 length + UTF-8 bytes.</summary>
        private static string ReadOsuString(BinaryReader r)
        {
            byte kind = r.ReadByte();
            if (kind != 0x0b)
                return string.Empty;

            int len = 0, shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                len |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }

            return Encoding.UTF8.GetString(r.ReadBytes(len));
        }
    }
}
