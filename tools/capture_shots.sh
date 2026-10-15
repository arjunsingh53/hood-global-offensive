#!/usr/bin/env bash
# Regenerates the screenshot artifacts in shots/.
#
# Each capture runs a real match, waits for the requested state (alive, enemy in
# view, zoom fully raised for the scoped frame) and then renders genuine frames.
# Verify the results afterwards with tools/verify_shots.py.
set -euo pipefail

export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

mkdir -p shots

shot() {
  local out="$1"; shift
  echo "=== $out"
  dotnet run -- --screenshot "$out" "$@"
}

shot shots/00-menu.png       --seconds 0  --screen menu
shot shots/01-ak47.png       --seconds 6  --weapon AK-47   --seed 20260114
shot shots/02-deagle.png     --seconds 6  --weapon "Desert Eagle" --seed 7331
shot shots/03-awp-scoped.png --seconds 7  --weapon AWP     --seed 4242 --zoom
shot shots/04-buy-menu.png   --seconds 2  --screen buy     --seed 20260114
shot shots/05-scoreboard.png --seconds 200 --screen scoreboard --seed 20260114
shot shots/06-glock17.png    --seconds 1  --weapon "Glock 17" --seed 20260114

echo
echo "captured:"
ls -1 shots/
