using System;
using System.Diagnostics;

namespace OsuBeatmapEditor.Game.Beatmaps
{
    /// <summary>
    /// Hands an <c>.osz</c> archive to osu!lazer so its own importer adds it to the library.
    /// This keeps the editor a pure intermediary: osu!lazer validates, hashes and stores the map.
    /// </summary>
    public static class LazerImporter
    {
        /// <summary>
        /// Opens the archive with osu!lazer (launching it if necessary). Returns false if the
        /// platform launch could not be started.
        /// </summary>
        public static bool Import(string oszPath)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // Forward the file to the running "osu!" app in the background (-g) so it imports
                    // without stealing focus; it still launches osu! if it isn't already running.
                    var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                    psi.ArgumentList.Add("-g");
                    psi.ArgumentList.Add("-a");
                    psi.ArgumentList.Add("osu!");
                    psi.ArgumentList.Add(oszPath);
                    Process.Start(psi);
                    return true;
                }

                if (OperatingSystem.IsWindows())
                {
                    // Open with the OS default handler for .osz (osu!lazer registers itself).
                    Process.Start(new ProcessStartInfo(oszPath) { UseShellExecute = true });
                    return true;
                }

                // Linux and others.
                Process.Start(new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { oszPath } });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
