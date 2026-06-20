using System;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Validates binary assets (audio, background image) downloaded from a collab before they are written to lazer's
    /// file store. Shared collab content comes from other users, so it is untrusted: we cap its size (to stop disk
    /// fills / decompression bombs), require a real audio/image extension, and sniff the leading "magic" bytes so a
    /// renamed executable or other unexpected blob is rejected rather than handed to a media decoder. Storage is
    /// content-addressed by hash and nothing is ever executed, so this is defence-in-depth, not the only barrier.
    /// </summary>
    public static class CollabAssetValidator
    {
        /// <summary>Largest accepted audio asset (20 MB) - generous for a full song, far below a disk-fill.</summary>
        public const long MaxAudioBytes = 20L * 1024 * 1024;

        /// <summary>Largest accepted background image (10 MB).</summary>
        public const long MaxImageBytes = 10L * 1024 * 1024;

        /// <summary>Validates a collab audio asset. Returns null when acceptable, else a user-facing reason.</summary>
        public static string? ValidateAudio(byte[] bytes, string filename)
        {
            if (bytes == null || bytes.Length == 0)
                return "The audio asset is empty.";
            if (bytes.Length > MaxAudioBytes)
                return $"The audio asset is too large ({bytes.Length / (1024 * 1024)} MB; max {MaxAudioBytes / (1024 * 1024)} MB).";
            if (!hasExtension(filename, ".mp3", ".ogg", ".wav"))
                return "The audio asset has an unsupported file type.";
            if (!looksLikeAudio(bytes))
                return "The audio asset isn't a recognised audio file.";
            return null;
        }

        /// <summary>Validates a collab background image. Returns null when acceptable, else a user-facing reason.</summary>
        public static string? ValidateImage(byte[] bytes, string filename)
        {
            if (bytes == null || bytes.Length == 0)
                return "The background asset is empty.";
            if (bytes.Length > MaxImageBytes)
                return $"The background asset is too large ({bytes.Length / (1024 * 1024)} MB; max {MaxImageBytes / (1024 * 1024)} MB).";
            if (!hasExtension(filename, ".jpg", ".jpeg", ".png"))
                return "The background asset has an unsupported file type.";
            if (!looksLikeImage(bytes))
                return "The background asset isn't a recognised image file.";
            return null;
        }

        private static bool hasExtension(string filename, params string[] allowed)
        {
            if (string.IsNullOrEmpty(filename))
                return false;
            foreach (string ext in allowed)
                if (filename.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Magic-byte sniff for the audio containers osu! uses (MP3, OGG, WAV).</summary>
        private static bool looksLikeAudio(byte[] b)
        {
            // MP3: an "ID3" tag, or an MPEG frame sync (11 set bits: FF Ex/Fx).
            if (starts(b, (byte)'I', (byte)'D', (byte)'3'))
                return true;
            if (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0)
                return true;

            // OGG container ("OggS").
            if (starts(b, (byte)'O', (byte)'g', (byte)'g', (byte)'S'))
                return true;

            // WAV (RIFF .... WAVE).
            if (b.Length >= 12
                && starts(b, (byte)'R', (byte)'I', (byte)'F', (byte)'F')
                && b[8] == 'W' && b[9] == 'A' && b[10] == 'V' && b[11] == 'E')
                return true;

            return false;
        }

        /// <summary>Magic-byte sniff for the image formats osu! backgrounds use (JPEG, PNG).</summary>
        private static bool looksLikeImage(byte[] b)
        {
            // JPEG (FF D8 FF).
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return true;

            // PNG (89 50 4E 47 0D 0A 1A 0A).
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
                return true;

            return false;
        }

        private static bool starts(byte[] b, params byte[] prefix)
        {
            if (b.Length < prefix.Length)
                return false;
            for (int i = 0; i < prefix.Length; i++)
                if (b[i] != prefix[i])
                    return false;
            return true;
        }
    }
}
