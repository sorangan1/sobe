using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osuTK;
using osuTK.Input;

namespace OsuBeatmapEditor.Game.Statistics
{
    /// <summary>
    /// App-wide usage statistics: how long the editor has been open, and active editing time
    /// (mouse/interaction-driven, idle-aware) accumulated per beatmap and in total. Persisted to app
    /// storage and committed continuously, so a crash loses at most a few seconds of accounting.
    /// </summary>
    public partial class StatisticsTracker : Drawable
    {
        private const string filename = "editor-stats.json";

        /// <summary>No interaction for this long marks the user as idle; active time stops accruing.</summary>
        private const double idle_threshold_ms = 10 * 60 * 1000;

        /// <summary>Re-entering the same map within this gap resumes the previous session counter (so an
        /// accidental click out of the map doesn't reset the current editing-session readout).</summary>
        private const double session_resume_grace_ms = 2 * 60 * 1000;

        /// <summary>How often accumulated stats are flushed to disk.</summary>
        private const double save_interval_ms = 15 * 1000;

        private readonly Storage? storage;

        private double totalOpenMs;
        private double totalActiveMs;
        private readonly Dictionary<string, double> perMapActiveMs = new Dictionary<string, double>();

        // Live tracking state.
        private double sessionTime;        // monotonic ms since construction
        private double lastInputTime;      // sessionTime at the last detected interaction
        private Vector2 lastMousePosition;
        private Vector2 lastScroll;
        private bool seededInput;

        private string? currentMapKey;
        private bool lastActive;
        private double currentSessionActiveMs;

        /// <summary>Human-readable name of the map currently open in the editor (e.g. "Artist - Title [Diff]"),
        /// or null when not in the editor. Used for presence reporting.</summary>
        public string? CurrentMapDisplay { get; private set; }

        /// <summary>True while a map is open and the user is not idle.</summary>
        public bool IsActivelyEditing => currentMapKey != null && lastActive;
        private string? lastExitedKey;
        private double lastExitTime = double.NegativeInfinity;

        private double sinceSave;

        public StatisticsTracker(Storage? storage = null)
        {
            this.storage = storage;
            // No visual footprint, but Update must always run.
            AlwaysPresent = true;
            load();
        }

        /// <summary>Total time the application has been open across all sessions.</summary>
        public double TotalOpenMs => totalOpenMs;

        /// <summary>Total active editing time summed across every map.</summary>
        public double TotalActiveMs => totalActiveMs;

        /// <summary>Active editing time accumulated in the current map's editing session.</summary>
        public double CurrentSessionActiveMs => currentSessionActiveMs;

        public double ActiveMsForMap(string key) => perMapActiveMs.TryGetValue(key, out double v) ? v : 0;

        /// <summary>A snapshot of accumulated active editing time per map key (ms). Safe to enumerate; it's a copy.</summary>
        public IReadOnlyDictionary<string, double> PerMapActiveMsSnapshot() => new Dictionary<string, double>(perMapActiveMs);

        /// <summary>Total active editing time recorded for the map currently being edited (all sessions).</summary>
        public double ActiveMsForCurrentMap => currentMapKey != null ? ActiveMsForMap(currentMapKey) : 0;

        /// <summary>A stable per-map key independent of the .osu hash (which changes on every save).</summary>
        public static string MapKey(string artist, string title, string author, string difficulty)
            => $"{artist}|{title}|{author}|{difficulty}";

        /// <summary>Turns a <see cref="MapKey"/> back into a display name "Artist - Title [Diff]".</summary>
        private static string displayFromKey(string key)
        {
            string[] p = key.Split('|');
            if (p.Length >= 4)
                return $"{p[0]} - {p[1]} [{p[3]}]";
            return key;
        }

        /// <summary>Begins (or resumes) an editing session for the given map.</summary>
        public void EnterMap(string key)
        {
            currentMapKey = key;
            CurrentMapDisplay = displayFromKey(key);

            // If the user just stepped out of this same map, keep the running session counter; otherwise
            // start a fresh session.
            bool resume = key == lastExitedKey && (sessionTime - lastExitTime) <= session_resume_grace_ms;
            if (!resume)
                currentSessionActiveMs = 0;

            lastInputTime = sessionTime;
        }

        /// <summary>Ends the current editing session, remembering it briefly so a quick re-entry resumes it.</summary>
        public void ExitMap()
        {
            lastExitedKey = currentMapKey;
            lastExitTime = sessionTime;
            currentMapKey = null;
            CurrentMapDisplay = null;
            save();
        }

        protected override void Update()
        {
            base.Update();

            double elapsed = Clock.ElapsedFrameTime;
            if (elapsed <= 0)
                return;

            sessionTime += elapsed;
            totalOpenMs += elapsed;

            if (detectActivity())
                lastInputTime = sessionTime;

            bool active = (sessionTime - lastInputTime) <= idle_threshold_ms;
            lastActive = active;
            if (active && currentMapKey != null)
            {
                perMapActiveMs[currentMapKey] = ActiveMsForMap(currentMapKey) + elapsed;
                totalActiveMs += elapsed;
                currentSessionActiveMs += elapsed;
            }

            sinceSave += elapsed;
            if (sinceSave >= save_interval_ms)
            {
                sinceSave = 0;
                save();
            }
        }

        /// <summary>True if the user interacted this frame (mouse moved/clicked/scrolled).</summary>
        private bool detectActivity()
        {
            var input = GetContainingInputManager();
            if (input == null)
                return false;

            var mouse = input.CurrentState.Mouse;
            Vector2 pos = mouse.Position;
            Vector2 scroll = mouse.Scroll;
            bool buttonHeld = mouse.IsPressed(MouseButton.Left)
                              || mouse.IsPressed(MouseButton.Right)
                              || mouse.IsPressed(MouseButton.Middle);

            bool moved = !seededInput || pos != lastMousePosition || scroll != lastScroll || buttonHeld;

            lastMousePosition = pos;
            lastScroll = scroll;
            seededInput = true;
            return moved;
        }

        protected override void Dispose(bool isDisposing)
        {
            save();
            base.Dispose(isDisposing);
        }

        /// <summary>Formats a duration as e.g. "2h 13m", "13m 04s" or "42s".</summary>
        public static string Format(double ms)
        {
            long totalSeconds = (long)(Math.Max(0, ms) / 1000);
            long h = totalSeconds / 3600;
            long m = (totalSeconds % 3600) / 60;
            long s = totalSeconds % 60;

            if (h > 0)
                return $"{h}h {m:00}m";
            if (m > 0)
                return $"{m}m {s:00}s";
            return $"{s}s";
        }

        private void load()
        {
            if (storage == null || !storage.Exists(filename))
                return;

            try
            {
                using var stream = storage.GetStream(filename, FileAccess.Read, FileMode.Open);
                using var reader = new StreamReader(stream);
                var data = JsonSerializer.Deserialize<StatsData>(reader.ReadToEnd());
                if (data == null)
                    return;

                totalOpenMs = data.TotalOpenMs;
                totalActiveMs = data.TotalActiveMs;
                if (data.PerMapActiveMs != null)
                {
                    foreach (var (key, value) in data.PerMapActiveMs)
                        perMapActiveMs[key] = value;
                }
            }
            catch
            {
                // A corrupt stats file should never break the app; start fresh.
            }
        }

        private void save()
        {
            if (storage == null)
                return;

            try
            {
                var data = new StatsData
                {
                    TotalOpenMs = totalOpenMs,
                    TotalActiveMs = totalActiveMs,
                    PerMapActiveMs = perMapActiveMs,
                };

                using var stream = storage.GetStream(filename, FileAccess.Write, FileMode.Create);
                using var writer = new StreamWriter(stream);
                writer.Write(JsonSerializer.Serialize(data));
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        private class StatsData
        {
            public double TotalOpenMs { get; set; }
            public double TotalActiveMs { get; set; }
            public Dictionary<string, double>? PerMapActiveMs { get; set; }
        }
    }
}
