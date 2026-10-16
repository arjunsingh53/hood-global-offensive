#!/usr/bin/env bash
# Regenerates the screenshot artifacts in shots/.
#
# Each capture runs the real game, waits for the requested state (alive, enemy in
# view, ADS fully raised when requested) and then renders genuine frames. Verify
# the curated public set afterwards with tools/verify_shots.py.
set -euo pipefail

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
# A normal shell resolves `dotnet` from PATH.  WSL launched by a Windows host
# may instead need its Windows SDK executable supplied explicitly, for example:
# DOTNET_BIN=/mnt/c/Users/you/.dotnet/dotnet.exe bash tools/capture_shots.sh
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
if ! command -v "$DOTNET_BIN" >/dev/null 2>&1 && [[ ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet SDK not found; set DOTNET_BIN to the dotnet executable" >&2
  exit 127
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

mkdir -p shots

shot() {
  local out="$1"; shift
  echo "=== $out"
  "$DOTNET_BIN" run -- --screenshot "$out" "$@"
}

shot shots/20-sunset-menu.png      --seconds 0 --screen menu
shot shots/01-ak47.png             --seconds 6 --weapon AK-47 --seed 20260114 --zoom
shot shots/04-buy-menu.png         --seconds 2 --screen buy --seed 20260114
shot shots/07-industrial-zone.png  --seconds 6 --weapon AK-47 --seed 20260114
shot shots/21-capsule-sunset-showcase.png --seconds 0 --screen character-idle --weapon AK-47 --seed 20260114
shot shots/22-capsule-impact-sunset.png   --seconds 0 --screen impact --weapon AK-47 --seed 20260114

echo
echo "captured:"
ls -1 shots/
