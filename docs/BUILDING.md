# Building and running

## Requirements

| Requirement | Version | Notes |
| --- | --- | --- |
| .NET SDK | 8.0 or newer | The project targets `net10.0` and was developed against 10.0.402 |
| GPU + display | — | Only for playing; `--headless` needs neither |
| Python | 3.8+ | Only for the screenshot verification scripts |
| bash | any | Only for `tools/capture_shots.sh` (Git Bash is fine on Windows) |

The sole NuGet dependency is [`Raylib-cs`](https://github.com/ChrisDill/Raylib-cs) 8.1.0, which
carries the native raylib binaries for Windows, Linux and macOS. There is nothing else to install.

The runtime art assets include `assets/maps/low-poly-industrial-zone.glb`,
`assets/weapons/glock17.glb`, `assets/weapons/ak47-viewmodel.glb`,
`assets/weapons/inhaler-keychain.glb`, the preserved fallback `assets/characters/soldier.glb`, and
`assets/characters/tactical-soldier-02.glb`; the build
copies `assets/**` next to the executable so the game finds them whichever directory it is launched
from. The weapon source is optimised with [gltfpack](https://github.com/zeux/meshoptimizer/releases)
and Pillow; see [tools/build_weapon_asset.py](../tools/build_weapon_asset.py). The Soldier conversion
uses Pillow only and preserves its skinning/animation data; see
[tools/build_character_asset.py](../tools/build_character_asset.py).
The original project-authored AK source and axis contract are documented in
[ak47-viewmodel-README.md](../assets/weapons/ak47-viewmodel-README.md), with its rebuild recipe at
[build_ak47_viewmodel.py](../tools/build_ak47_viewmodel.py).

### Industrial Zone map attribution

`assets/maps/low-poly-industrial-zone.glb` is the project-owner-supplied Low Poly Industrial Zone
by novikto, licensed CC-BY 4.0. It is copied byte-for-byte from the supplied source rather than
regenerated. Keep the bundled [attribution and provenance record](../assets/maps/LOW-POLY-INDUSTRIAL-ZONE-ATTRIBUTION.md)
with any distributed build; see the [collision/render integration note](LOW-POLY-INDUSTRIAL-ZONE-INTEGRATION.md)
for the deterministic sidecar design.

### Tactical Soldier 02 rebuild

The Tactical Soldier runtime GLB is generated from the user-supplied FBX, never
from the incomplete OBJ. It needs Blender because the upload has a skeleton and
textures but no playable locomotion clips. The conversion recipe uses a portable
Blender build in a temporary `F:` workspace, creates HGO-local presentation
loops, bakes the FBX armature's 0.01 scale (required for correct Raylib
animation), and then transcodes every embedded image to PNG.

```powershell
$blender = 'F:\WORK\cli-gui\.tooling\blender-5.2.2\blender-5.2.2-windows-x64\blender.exe'
$stage = 'F:\WORK\cli-gui\.tmp-model-import\tactical-soldier-02'

& $blender --factory-startup --disable-autoexec --background `
  --python tools\convert_tactical_soldier.py -- `
  "$stage\source\Tactical_Soldier_02.fbx" "$stage\textures" "$stage\tactical-soldier-02.raw.glb"

python tools\finalize_tactical_soldier.py `
  --input "$stage\tactical-soldier-02.raw.glb" `
  --output assets\characters\tactical-soldier-02.glb
```

The finalizer rejects a package with other than one skin, required glTF
extensions, morph targets, additional UV/influence attributes, non-PNG maps, or
missing `Idle`/`Walk`/`Run`/`Crouch` actions. See the source and licence boundary
in [Tactical-Soldier-02-SOURCE.md](../assets/characters/Tactical-Soldier-02-SOURCE.md).

---

## Everyday commands

```bash
dotnet build                 # compile
dotnet run                   # play
dotnet run -- --help         # list every option
```

### Verify a change

```bash
# full match, no window, asserts invariants and exits non-zero on failure
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114

# focused deterministic movement trace: sprint/tactical timing, stance, air transition and replay
dotnet run -- --movement-check --seed 20260114

# focused deterministic weapon-readiness trace: selector, inspect cancellation, sprint pose and replay
dotnet run -- --weapon-check --seed 20260114
```

The full-match report ends in `RESULT: PASS` or `RESULT: FAIL`. Pair it with the movement and weapon-readiness checks
whenever changing player physics or first-person feedback: that focused trace proves sprint input
edges, tactical-sprint duration/stamina, crouch cancellation, jump/air cancellation, collision
safety, a real landing-feedback envelope, render-only FOV feedback, aim-direction preservation and
deterministic replay; the weapon trace separately proves selector defaults, semi/automatic behavior,
inspect cancellation, local sprint pose, aim preservation and deterministic replay before the
match-level check exercises combat, scoring and respawning.

---

## Verification tooling

The `tools/` directory holds the checks that guard the visual output. They are deliberately written
against the real PNG artifacts in `shots/` rather than against intermediate state, so they test what
actually ships.

### `tools/capture_shots.sh`

Regenerates every screenshot artifact by running the game. Each capture plays a real match, waits
for the required state (alive, healthy, enemy in view, and for the scoped frame a scope at least 97%
raised), then renders genuine frames.

```bash
bash tools/capture_shots.sh
```

Use this after any change to the renderer, the HUD or the menus.

### `tools/verify_shots.py`

Decodes the PNGs with the Python standard library only — no Pillow — and measures where the view
model and HUD actually land. It asserts:

- the weapon is present in the lower right and does not reach into the crosshair region
- the scoped frame really is scoped, using a corner/centre brightness ratio (colours are unreliable
  here because the scope mask alpha-blends level geometry)
- the title screen renders its accent bar and a title that is not clipped by the window edge
- the buy menu and scoreboard overlays draw their expected content

It also contains a **positive control**: the same statistic that proves a weapon is *stowed* in the
scoped frame is measured on a frame whose weapon is drawn, to show the check is capable of detecting
a gun at all. A check that cannot fail is not a check.

```bash
python tools/verify_shots.py      # exits non-zero on any failure
```

### `tools/sweep_weapons.py`

Runs the framing check across all eight weapons at five different moments each, which catches
animation states — reload dip, recoil kick, landing — that move the weapon somewhere it should not
be. It invokes the built executable directly and takes about a minute.

```bash
python tools/sweep_weapons.py
python tools/sweep_weapons.py --exe bin/Release/net10.0/HoodGlobalOffensive.exe
```

### `tools/inspect_png.py`

A dependency-free PNG decoder used by the scripts above. It is also handy on its own for inspecting
any PNG, and it is importable:

```python
import sys; sys.path.insert(0, "tools")
from inspect_png import decode
w, h, bpp, px = decode("shots/01-ak47.png")
```

---

## Distributing a build

To produce a self-contained build that runs without a .NET installation:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Swap `win-x64` for `linux-x64` or `osx-arm64` as needed. The output lands in
`bin/Release/net10.0/<rid>/publish/`.

> **Note for the development machine this project was built on:** a Windows application-control
> policy blocks newly built Release executables with a `FileLoadException`, so verification was done
> against the default Debug configuration. This is a machine policy, not a property of the code —
> `dotnet run` works normally. If you hit the same thing, use the default configuration.

### A note on `DOTNET_ROOT`

If the SDK is installed per-user rather than system-wide — for example in `%USERPROFILE%\.dotnet` —
then running the built `.exe` directly requires `DOTNET_ROOT` to point at it:

```bash
export DOTNET_ROOT="$HOME/.dotnet"    # Git Bash / Linux / macOS
set DOTNET_ROOT=%USERPROFILE%\.dotnet # Windows cmd
```

`dotnet run` handles this for you, and so does `play.cmd` on Windows.

---

## Project layout

```
HoodGlobalOffensive.csproj   the only project file
Program.cs                   entry point
play.cmd                     Windows launcher (double-click to play)
src/                         all source, organised by layer
tools/                       capture and verification scripts
shots/                       verified screenshot artifacts
docs/                        this documentation
gallery.html                 standalone screenshot gallery page
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for what lives where and why.

---

## Code style

The codebase follows the standard .NET conventions:

- File-scoped namespaces.
- Nullable reference types enabled; implicit usings enabled.
- One type per file, named after the file.
- XML doc comments on public types and non-obvious members, explaining *why* rather than restating
  the signature.
- Gameplay constants live in `Cfg.cs` or in a definition table, never inline at the point of use.

The build is clean: **0 warnings, 0 errors**.
