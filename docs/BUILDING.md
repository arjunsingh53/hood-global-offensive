# Building and running

## Requirements

| Requirement | Version | Notes |
| --- | --- | --- |
| .NET SDK | 8.0 or newer | The project targets `net10.0` and was developed against 10.0.402 |
| GPU + display | — | Only for playing; `--headless` needs neither |
| Python | 3.8+ | Only for the screenshot verification scripts |
| bash | any | Only for `tools/capture_shots.sh` (Git Bash is fine on Windows) |

The sole NuGet dependency is [`Raylib-cs`](https://github.com/ChrisDill/Raylib-cs) 8.1.0, which
carries the native raylib binaries for Windows, Linux and macOS. There is nothing else to install,
and there are no binary assets in the repository.

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
```

The report ends in `RESULT: PASS` or `RESULT: FAIL`. This is the single most useful check in the
project: it exercises movement, collision, combat, damage, scoring and respawning in one run.

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
