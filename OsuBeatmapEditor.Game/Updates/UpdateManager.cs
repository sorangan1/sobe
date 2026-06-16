using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace OsuBeatmapEditor.Game.Updates
{
    public enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        UpdateAvailable,
        Downloading,
        ReadyToRestart,
        Failed,
    }

    /// <summary>
    /// Self-update mechanism for the standalone (self-contained) desktop builds. Checks the project's GitHub
    /// releases for a newer version, downloads the platform-matching zip, stages it, and on restart swaps the
    /// installed files via a small detached helper script. Pure mechanism: the calling screen decides policy
    /// (auto vs manual, prompts). Cached app-wide and added to the tree so its <see cref="Drawable.Scheduler"/>
    /// can marshal background work back onto the update thread.
    /// </summary>
    public partial class UpdateManager : Drawable
    {
        private const string repo = "sorangan1/sobe";
        private const string latest_release_api = "https://api.github.com/repos/" + repo + "/releases/latest";
        private const string releases_page = "https://github.com/" + repo + "/releases/latest";

        [Resolved(CanBeNull = true)]
        private GameHost? host { get; set; }

        public readonly Bindable<UpdateState> State = new Bindable<UpdateState>(UpdateState.Idle);

        /// <summary>The newest version string reported by GitHub (without the leading "v"), once known.</summary>
        public readonly Bindable<string> LatestVersion = new Bindable<string>(string.Empty);

        /// <summary>Download progress, 0..1, while <see cref="UpdateState.Downloading"/>.</summary>
        public readonly BindableFloat Progress = new BindableFloat();

        private ReleaseInfo? pendingRelease;
        private string? stagedScriptPath;
        private bool checkStarted;

        public UpdateManager()
        {
            AlwaysPresent = true;
        }

        /// <summary>The GitHub releases page, for the manual-download fallback.</summary>
        public string ReleasesPageUrl => releases_page;

        /// <summary>
        /// True when the running process looks like a packaged self-contained build we can swap in place
        /// (i.e. not a `dotnet run` dev session), so an in-app install can actually succeed.
        /// </summary>
        public bool CanSelfInstall => resolveInstallTarget() != null;

        /// <summary>Kicks off a one-time background version check (safe to call repeatedly; only the first runs).</summary>
        public void CheckForUpdatesOnce()
        {
            if (checkStarted)
                return;

            checkStarted = true;
            _ = checkAsync();
        }

        private async Task checkAsync()
        {
            Schedule(() => State.Value = UpdateState.Checking);

            try
            {
                var release = await fetchLatestReleaseAsync().ConfigureAwait(false);
                Schedule(() =>
                {
                    // GitHub's /releases/latest already returns the newest published release, so we treat it as
                    // authoritative: an update is available whenever it differs from what's installed. (We avoid a
                    // strict numeric "greater than" so a new release supersedes an older one even if its version
                    // sorts lower numerically - e.g. 0.9.6 replacing a mistakenly-numbered 0.9.41.)
                    if (release == null || !differsFromCurrent(release.Version, AppInfo.Version))
                    {
                        State.Value = UpdateState.UpToDate;
                        return;
                    }

                    pendingRelease = release;
                    LatestVersion.Value = release.Version;
                    State.Value = UpdateState.UpdateAvailable;
                });
            }
            catch (Exception e)
            {
                Logger.Log($"Update check failed: {e.Message}", LoggingTarget.Network);
                Schedule(() => State.Value = UpdateState.Failed);
            }
        }

        /// <summary>
        /// Downloads and stages the pending update (no-op unless one is available). On success the state
        /// becomes <see cref="UpdateState.ReadyToRestart"/>; call <see cref="RestartAndApply"/> to finish.
        /// </summary>
        public async Task<bool> PrepareAsync()
        {
            var release = pendingRelease;
            if (release == null || State.Value == UpdateState.Downloading || State.Value == UpdateState.ReadyToRestart)
                return State.Value == UpdateState.ReadyToRestart;

            var asset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, expectedAssetName(), StringComparison.OrdinalIgnoreCase));
            string? installTarget = resolveInstallTarget();
            if (asset == null || installTarget == null)
            {
                // Nothing we can install here; let the caller fall back to opening the release page.
                Schedule(() => State.Value = UpdateState.UpdateAvailable);
                return false;
            }

            Schedule(() =>
            {
                Progress.Value = 0;
                State.Value = UpdateState.Downloading;
            });

            try
            {
                string script = await Task.Run(() => downloadStageAndScript(asset, installTarget)).ConfigureAwait(false);
                stagedScriptPath = script;
                Schedule(() => State.Value = UpdateState.ReadyToRestart);
                return true;
            }
            catch (Exception e)
            {
                Logger.Log($"Update download failed: {e.Message}", LoggingTarget.Network);
                Schedule(() => State.Value = UpdateState.Failed);
                return false;
            }
        }

        /// <summary>Launches the detached installer script and quits, so the swap can complete and relaunch us.</summary>
        public void RestartAndApply()
        {
            if (stagedScriptPath == null)
                return;

            try
            {
                launchInstaller(stagedScriptPath);
                host?.Exit();
            }
            catch (Exception e)
            {
                Logger.Log($"Failed to launch installer: {e.Message}", LoggingTarget.Network);
                State.Value = UpdateState.Failed;
            }
        }

        /// <summary>Opens the GitHub releases page in the user's browser (manual-download fallback).</summary>
        public void OpenReleasesPage() => host?.OpenUrlExternally(releases_page);

        // ---- GitHub API ----

        private static async Task<ReleaseInfo?> fetchLatestReleaseAsync()
        {
            using var client = createClient();
            using var response = await client.GetAsync(latest_release_api).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<ReleaseDto>(json);
            if (dto?.TagName == null)
                return null;

            return new ReleaseInfo(
                normaliseVersion(dto.TagName),
                (dto.Assets ?? new List<AssetDto>())
                    .Where(a => a.Name != null && a.BrowserDownloadUrl != null)
                    .Select(a => new AssetInfo(a.Name!, a.BrowserDownloadUrl!))
                    .ToList());
        }

        private static HttpClient createClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("sobe-updater", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromMinutes(10);
            return client;
        }

        // ---- Download + staging ----

        private string downloadStageAndScript(AssetInfo asset, string installTarget)
        {
            string work = Path.Combine(Path.GetTempPath(), "sobe-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            string zipPath = Path.Combine(work, asset.Name);
            downloadWithProgress(asset.Url, zipPath);

            string stageDir = Path.Combine(work, "stage");
            Directory.CreateDirectory(stageDir);
            ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);

            return writeInstallerScript(work, stageDir, installTarget);
        }

        private void downloadWithProgress(string url, string destination)
        {
            using var client = createClient();
            using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            using var source = response.Content.ReadAsStream();
            using var file = File.Create(destination);

            byte[] buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                file.Write(buffer, 0, n);
                read += n;
                if (total is > 0)
                {
                    float p = (float)read / total.Value;
                    Schedule(() => Progress.Value = p);
                }
            }
        }

        // ---- Installer scripts (per-OS) ----

        private string writeInstallerScript(string workDir, string stageDir, string installTarget)
        {
            int pid = Environment.ProcessId;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: stage contains "sobe.app"; replace the existing bundle and relaunch.
                string stagedApp = Path.Combine(stageDir, "sobe.app");
                string script = Path.Combine(workDir, "install.sh");
                File.WriteAllText(script,
                    "#!/bin/bash\n" +
                    "set -e\n" +
                    $"PID={pid}\n" +
                    $"STAGE={quote(stagedApp)}\n" +
                    $"TARGET={quote(installTarget)}\n" +
                    "while kill -0 \"$PID\" 2>/dev/null; do sleep 0.3; done\n" +
                    "sleep 0.5\n" +
                    "rm -rf \"$TARGET\"\n" +
                    "mv \"$STAGE\" \"$TARGET\"\n" +
                    "xattr -dr com.apple.quarantine \"$TARGET\" 2>/dev/null || true\n" +
                    "open \"$TARGET\"\n");
                return script;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string exe = Path.GetFileName(Environment.ProcessPath!);
                string script = Path.Combine(workDir, "install.sh");
                File.WriteAllText(script,
                    "#!/bin/bash\n" +
                    "set -e\n" +
                    $"PID={pid}\n" +
                    $"STAGE={quote(stageDir)}\n" +
                    $"TARGET={quote(installTarget)}\n" +
                    $"EXE={quote(exe)}\n" +
                    "while kill -0 \"$PID\" 2>/dev/null; do sleep 0.3; done\n" +
                    "sleep 0.5\n" +
                    "cp -rf \"$STAGE\"/. \"$TARGET\"/\n" +
                    "chmod +x \"$TARGET/$EXE\"\n" +
                    "( cd \"$TARGET\" && nohup \"./$EXE\" >/dev/null 2>&1 & )\n");
                return script;
            }

            // Windows: stage contains the files flat; wait for the PID, copy over, relaunch the exe.
            string winExe = Path.GetFileName(Environment.ProcessPath!);
            string bat = Path.Combine(workDir, "install.bat");
            File.WriteAllText(bat,
                "@echo off\r\n" +
                "setlocal\r\n" +
                $"set PID={pid}\r\n" +
                $"set STAGE={stageDir}\r\n" +
                $"set TARGET={installTarget}\r\n" +
                $"set EXE={winExe}\r\n" +
                ":waitloop\r\n" +
                "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul\r\n" +
                "if not errorlevel 1 (\r\n" +
                "  timeout /t 1 /nobreak >nul\r\n" +
                "  goto waitloop\r\n" +
                ")\r\n" +
                "xcopy /E /Y /I /Q \"%STAGE%\\*\" \"%TARGET%\\\" >nul\r\n" +
                "start \"\" \"%TARGET%\\%EXE%\"\r\n");
            return bat;
        }

        private void launchInstaller(string scriptPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                return;
            }

            // Unix: make executable, then run detached via bash. The script waits for our PID to exit.
            try { Process.Start("chmod", $"+x {quote(scriptPath)}")?.WaitForExit(2000); }
            catch { /* best-effort */ }

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = quote(scriptPath),
                UseShellExecute = false,
            });
        }

        // ---- Helpers ----

        /// <summary>The directory or bundle to overwrite when installing, or null if we can't determine one
        /// (e.g. running under `dotnet run`).</summary>
        private static string? resolveInstallTarget()
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return null;

            string name = Path.GetFileName(exe);
            // A dev run hosts us under the dotnet/testhost executable rather than our own binary.
            if (name.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase) || name.Contains("testhost", StringComparison.OrdinalIgnoreCase))
                return null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                int idx = exe.IndexOf(".app/", StringComparison.Ordinal);
                if (idx < 0)
                    return null; // running loose, not as a bundle - can't swap a .app
                return exe.Substring(0, idx + 4); // ".../sobe.app"
            }

            return Path.GetDirectoryName(exe);
        }

        private static string expectedAssetName()
        {
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
                : "linux";

            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

            // Windows/Linux only ship x64; macOS ships both arm64 and x64.
            if (os != "macos")
                arch = "x64";

            return $"sobe-{os}-{arch}.zip";
        }

        private static string normaliseVersion(string tag)
        {
            string v = tag.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            return v;
        }

        /// <summary>True if the latest released version differs from the one currently running.</summary>
        private static bool differsFromCurrent(string candidate, string current)
        {
            return parse(candidate) != parse(current);

            static Version parse(string s)
            {
                // Keep only the leading numeric core, e.g. "0.9.3-beta" -> "0.9.3".
                string core = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
                return Version.TryParse(core, out var v) ? v : new Version(0, 0, 0);
            }
        }

        private static string quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

        private record ReleaseInfo(string Version, List<AssetInfo> Assets);

        private record AssetInfo(string Name, string Url);

        private class ReleaseDto
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
        }

        private class AssetDto
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        }
    }
}
