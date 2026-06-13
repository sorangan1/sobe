# sobe — sorangan osu! beatmap editor

A standalone osu!-style beatmap editor built on [osu!Framework](https://github.com/ppy/osu-framework).
It reads the beatmaps already installed in your **osu!lazer** library, lets you edit them
(objects, timing, hitsounds, metadata, colours…) and writes the changes straight back into
lazer's database — no `.osz` round-trip, no launching osu!.

> 🤖 **Vibecoded.** sobe was built largely by "vibe coding" — iterating with an AI assistant
> rather than hand-writing every line. Treat the code accordingly.

## Features

- Full compose view: circles, sliders (multi-anchor + control-point editing), spinners
- Top timeline with hitsound lanes (Whistle/Finish/Clap, per-node banks)
- Timing-point editor (BPM/SV/kiai), distance snap, convert-to-stream
- Song setup (metadata + difficulty), editable combo/map colours
- Customisable editor settings and keyboard shortcuts
- Reads + saves directly to the osu!lazer realm

## Download

Grab a build for your platform from the [**Releases**](../../releases) page:

- **Windows** — `sobe-windows-x64.zip` → unzip and run `OsuBeatmapEditor.exe`
- **macOS** — `sobe-macos-arm64.zip` (Apple Silicon) or `sobe-macos-x64.zip` (Intel) → unzip
  and open `sobe.app`

osu!lazer must be installed, since sobe edits its library.

### Opening the unsigned builds

These builds are **not code-signed** (signing requires paid Apple/Windows certificates), so the OS
will warn you the first time. This is expected.

- **Windows:** SmartScreen shows "Windows protected your PC" → click **More info → Run anyway**.
- **macOS:** if you see "sobe can't be opened" or "is damaged", remove the download quarantine flag:

  ```bash
  xattr -dr com.apple.quarantine /path/to/sobe.app
  ```

  Then open it normally. (For a single try you can also **right-click → Open**, or allow it under
  *System Settings → Privacy & Security → Open Anyway*.)

## Building from source

Requires the **.NET 8 SDK**.

```bash
dotnet run --project OsuBeatmapEditor.Desktop
```

To produce a self-contained build manually:

```bash
dotnet publish OsuBeatmapEditor.Desktop -c Release -r osx-arm64 --self-contained
# or -r win-x64 / -r osx-x64
```

Releases are built automatically by [`.github/workflows/release.yml`](.github/workflows/release.yml)
when a `v*` tag is pushed.

## Acknowledgements

Built on [osu!Framework](https://github.com/ppy/osu-framework) and default-skin samples from
`ppy.osu.game.resources`. Not affiliated with ppy.

## License

[MIT](LICENSE).
