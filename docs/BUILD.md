# Building sobe

sobe is a .NET / [osu!Framework](https://github.com/ppy/osu-framework) desktop app. These instructions
cover building and running it on **Windows, macOS and Linux**.

## Prerequisites

- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
  (The projects target `net8.0`. A newer SDK can still build them — see [Note on SDK versions](#note-on-sdk-versions).)
- **git**
- A GPU with OpenGL support (the renderer needs it).
- **Linux only** — the runtime needs a few system libraries that are usually already present:
  - audio: `libasound2` (ALSA) and/or PulseAudio/PipeWire
  - graphics/input: `libgl1`, and the usual X11/Wayland libs
  - On Debian/Ubuntu: `sudo apt install libasound2 libgl1` is typically enough; SDL and BASS are
    bundled with the build.

## Clone

```bash
git clone https://github.com/sorangan1/sobe.git
cd sobe
```

## Run (development)

```bash
dotnet run --project OsuBeatmapEditor.Desktop
```

This builds and launches the editor. sobe reads the beatmaps from your installed **osu!lazer**
library, so lazer must be installed for there to be anything to edit.

## Publish a standalone build

Self-contained builds bundle the .NET runtime and all native libraries (SDL, ffmpeg, BASS), so the
target machine needs nothing installed. Pick the runtime identifier (RID) for your platform:

| Platform              | RID          |
|-----------------------|--------------|
| Windows x64           | `win-x64`    |
| macOS (Apple Silicon) | `osx-arm64`  |
| macOS (Intel)         | `osx-x64`    |
| Linux x64             | `linux-x64`  |

```bash
dotnet publish OsuBeatmapEditor.Desktop/OsuBeatmapEditor.Desktop.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o publish/linux-x64
```

The output lands in `publish/<rid>/`. Run it with:

- **Windows:** `OsuBeatmapEditor.exe`
- **macOS / Linux:** `./OsuBeatmapEditor` (you may need `chmod +x OsuBeatmapEditor` first)

> The published binaries are **unsigned**. See the README for how to clear the Windows SmartScreen /
> macOS Gatekeeper prompts. On Linux there is no such prompt — just make the file executable.

## Note on SDK versions

The projects target `net8.0`. If you only have a newer SDK installed (e.g. .NET 10), you can still build
and run via the solution filter, which rolls forward to the installed runtime:

```bash
dotnet build OsuBeatmapEditor.Desktop.slnf
```

For producing release binaries, installing the matching **.NET 8 SDK** is recommended (that's what CI uses).

## Releases (CI)

Pushing a `vX.Y.Z` tag triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml),
which publishes self-contained Windows, macOS (arm64/x64) and Linux x64 builds and attaches them to a
GitHub Release.
