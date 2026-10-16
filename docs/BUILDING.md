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
`assets/weapons/inhaler-keychain.glb`, the active
`assets/characters/hgo-lowpoly-tactical-operator.glb`, and the preserved fallback
`assets/characters/soldier.glb`; development builds copy `assets/**` next to the executable.
`assets/characters/tactical-soldier-02.glb` remains in the repository for provenance and rebuild
history, but is not the current runtime character. The Windows packaging recipe below uses .NET's
single-file self-extraction option for included runtime assets. The weapon source is optimised with
[gltfpack](https://github.com/zeux/meshoptimizer/releases) and Pillow; see
[tools/build_weapon_asset.py](../tools/build_weapon_asset.py).
The original project-authored AK source and axis contract are documented in
[ak47-viewmodel-README.md](../assets/weapons/ak47-viewmodel-README.md), with its rebuild recipe at
[build_ak47_viewmodel.py](../tools/build_ak47_viewmodel.py).

### Industrial Zone map attribution

`assets/maps/low-poly-industrial-zone.glb` is the project-owner-supplied Low Poly Industrial Zone
by novikto, licensed CC-BY 4.0. It is copied byte-for-byte from the supplied source rather than
regenerated. Keep the bundled [attribution and provenance record](../assets/maps/LOW-POLY-INDUSTRIAL-ZONE-ATTRIBUTION.md)
with any distributed build; see the [collision/render integration note](LOW-POLY-INDUSTRIAL-ZONE-INTEGRATION.md)
for the deterministic sidecar design.

### Runtime character: HGO Low-Poly Capsule Combatant

`assets/characters/hgo-lowpoly-tactical-operator.glb` is the active bot/world-character package.
It is original project art with `Idle`, `Walk`, `Run`, `Crouch` and `Prone` clips; its compact carbine
is a third-person world prop only and does not replace Gun Game's first-person weapon models or
hitboxes. See the [runtime asset record](../assets/characters/HGO-LOWPOLY-TACTICAL-OPERATOR.md) and
its repeatable [build script](../tools/build_lowpoly_tactical_operator.py).

### Optional sky panorama (not bundled)

The game always includes its own procedural dusk sky. A 2:1 equirectangular PNG may optionally be
placed at `assets/sky/hgo-sunset-panorama.png`; it is validated and camera-projected at runtime.
No third-party skybox is bundled, downloaded, or redistributed by this repository. Keep the source,
rights and attribution record with any panorama you add. See [the sky input contract](../assets/sky/README.md)
for resolution and packaging requirements.

### Tactical Soldier 02 rebuild — retained provenance, not active runtime art

The historical Tactical Soldier GLB was generated from the user-supplied FBX, never
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

# isolated profile round trip, corruption recovery and bounds checks; does not touch your real profile
dotnet run -- --settings-check

# deterministic listener routing: local/world scope, distance, pan and static-map occlusion profile
dotnet run -- --audio-check

# deterministic critical-health and actor-impact feedback contracts
dotnet run -- --combat-feedback-check --seed 20260114
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

Regenerates the current public screenshot set by running the game. Each capture plays a real match,
waits for the required state (alive, healthy, enemy in view, and for the AK iron-sight frame ADS at
least 97% raised), then renders genuine frames.

```bash
bash tools/capture_shots.sh
```

Use this after any change to the renderer, the HUD or the menus.

### `tools/verify_shots.py`

Decodes the PNGs with the Python standard library only — no Pillow — and measures the current
public frames. It asserts:

- the AK's real iron-sight geometry is visible but compact at the authoritative centre ray, and its
  scene corners remain visible (so ADS did not incorrectly invoke a scope mask)
- the title screen renders its accent bar and a title that is not clipped by the window edge
- the Buy Station overlay draws its accent and readable labels
- the Industrial Zone and HGO Capsule Combatant proof frames are full-size, non-empty live captures

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

## Packaging a local build

To produce a self-contained build that runs without a .NET installation:

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false -o release/win-x64
```

Swap `win-x64` for `linux-x64` or `osx-arm64` as needed. The local package lands in
`release/win-x64/` as one self-contained `HoodGlobalOffensive.exe`; it does not need a separate
.NET installation or an adjacent `assets/` folder. `EnableCompressionInSingleFile` keeps the
Windows package below GitHub's normal 100 MB file limit without changing the embedded assets.

> **Note:** the development `bin/Debug` executable is framework-dependent, while the packaging
> command above is self-contained. Use `dotnet run` for normal local development.

### A note on `DOTNET_ROOT`

If the SDK is installed per-user rather than system-wide — for example in `%USERPROFILE%\.dotnet` —
then running the **development** `.exe` directly requires `DOTNET_ROOT` to point at it. The
self-contained package executable does not:

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
