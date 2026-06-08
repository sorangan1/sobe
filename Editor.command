#!/bin/bash
#
# Double-clickable launcher for the osu! Beatmap Editor (macOS).
# Builds a Release binary on first run (and after code changes), then launches it.
#
set -e

# Always work from the folder this script lives in, regardless of where it's launched from.
cd "$(dirname "$0")"

PROJECT="OsuBeatmapEditor.Desktop/OsuBeatmapEditor.Desktop.csproj"
BINARY="OsuBeatmapEditor.Desktop/bin/Release/net8.0/OsuBeatmapEditor"

echo "Building osu! Beatmap Editor (Release)..."
dotnet build "$PROJECT" -c Release -v minimal

echo "Launching..."
exec "$BINARY"
