# Development handoff

> **Read this first if you are picking the project up (human or AI session).** It describes the
> exact state of the repository, the workflows that are easy to get wrong, and what is still open.
> Everything here is reproducible from a clean clone of `main` — the Unity migration phase 1 (the
> sim port) is committed in `8e670b9`; see "Migration state".
>
> **Keep this file current.** After every task, update "Where the project stands", the code map,
> and the open items here so the next session (or a friend on another machine) can continue from
> a fresh `git pull` alone. Commit the update with the task.

## Where the project stands

- **Release:** `0.2.2`, tagged `v0.2.2`, published on the
  [releases page](https://github.com/arjunsingh53/hood-global-offensive/releases) with a
  self-contained Windows x64 `HoodGlobalOffensive.exe` (assets embedded, no .NET install needed).
- **`main` = the v0.2.2 work plus follow-up commits:** the README screenshot gallery and main menu
  layout fix with `HANDOFF.md`/`AGENTS.md` (`9e782f9`), the recorded Unity migration plan
  (`6a6fbdd`), the Unity sim port with the golden-value parity harness (`8e670b9`), the
  handoff-per-task rule (`3ebec29`), the push policy (`a5ebde6`) and the shared parity suite with
  Unity edit-mode tests. The GitHub release asset for `v0.2.2` was rebuilt in place
  (`gh release upload --clobber`) so the downloadable exe includes the menu fix. Run
  `git log --oneline -5` for exact hashes.
- **Verified state:** `-warnaserror` build clean; both headless CI gates print `RESULT: PASS`;
  the parity harness prints `PARITY: PASS` (including byte-exact replays of both gates through the
  ported runner); the presentation compile gate (`tools/presentation-check`) builds clean; the
  single-file exe was re-verified from a clean directory.
- **Push policy:** `origin/main` is synced with `main`. The migration sync push (2026-10-16, at
  team request, so the other machine could pull the sim port and the edit-mode tests) brought
  GitHub up through `affecef` (the shared parity suite); see `git log --oneline -6`. Pre-push
  gate for future work: `-warnaserror` build clean, both CI gates `RESULT: PASS`, `tools/parity`
  `PARITY: PASS`, `tools/presentation-check` build clean, and `tools/editmode-check` build clean.
  After a push, update this bullet
  with the new sync point.
- **Unity migration (in progress — phase 1 committed `8e670b9`):** the deterministic simulation
  is ported into `unity/` and byte-verified against this repo by `tools/parity`. See "Migration
  state" below. The raylib build (`main`) remains the reference implementation and stays healthy.
- **Licensing:** MIT, **except** `assets/weapons/viewmodel-*.glb` (owner-supplied, not covered by
  the repo licence — see `assets/weapons/viewmodel-rig-SOURCE.md`) and the Industrial Zone map
  (CC-BY 4.0, novikto — keep its attribution file with any distribution).

### What the 0.2.2 pass changed

1. **Animated rig viewmodels** for Rifle / Sniper / SMG / Pistol (replacing legacy static meshes).
   Each rig has DRAW / IDLE / INSPEC / RELOAD1 / RELOAD2 / SHOOT clips. Hip placement and ADS
   were calibrated from captured frames; final calibrated ADS locals are in
   `src/Render/AnimatedViewmodels.cs`:
   - Rifle `(-10.5, 18, -30)`, SMG `(-11.2, 14, -30)`, Pistol `(-10.3, 7.75, -30)`
   - Sniper intentionally has no ADS local: the model stows and the scope overlay takes over.
2. **Audio rebuilt** in `src/Audio/WavGen.cs` — every gunshot/cue is filter-shaped noise through
   a small RBJ biquad chain (no saturated sine tones). `AudioBank` and the new
   `src/Audio/AudioAudit.cs` share one `WeaponFamilies` table so the live bank and the
   `--sfx-dump` audition WAVs can never drift. Also fixed: the heartbeat cue had been silently
   broken since 0.1 (envelope early-out ran at sample zero).
3. **Camera-anchored sky** in `src/Render/Renderer.cs`: `DrawProceduralSky(camera, w, h)` anchors
   the gradient, sun (`SunAzimuthDeg` / `SunElevationDeg`) and the 13 authored cumulus banks to
   the camera with correct pitch/yaw parallax.
4. **Main menu layout fix** (`src/UI/Screens.cs`): twelve rows had been overflowing into the
   footer hints; the design-canvas pitch is now 39 units from `y = 156` (row height 34, gap 5),
   keeping the last row at 619, above the first hint line at 624. **If you add a menu row,
   re-check this math.**
5. **README gallery** — eight 1920×1080 captures in `shots/` (numbered, force-added; see the
   gotcha about `.gitignore` below).

## Planned direction: complete migration to Unity

The team has decided to shift development of the game to **Unity completely**. Treat this
raylib codebase as the **reference implementation** the port works from — keep it healthy
(`main` builds clean, gates PASS) until the Unity version reaches parity, and use it to settle
behaviour questions.

- **Ports with intent (reference material):** gameplay rules and tuning tables (`Cfg`,
  `WeaponRegistry`, bot profiles), the deterministic-simulation design, the rig assets with
  their per-weapon calibration data and clip sets, and the procedural-audio recipes
  (`WavGen`'s biquad layers and three distance profiles).
- **Gets rebuilt in Unity:** rendering (raylib primitives → scene/prefabs/materials), input and
  settings plumbing, audio playback/output, HUD and menus, and the screenshot/capture tooling.
- **Rules that carry over unchanged:** the asset/licence split (rig pack and Industrial Zone map
  are owner-supplied/third-party, outside the MIT licence), and the verified-state discipline —
  port gameplay first, headless-test or playtest it, then presentation.

### Migration state (phase 1: sim port)

- The ported simulation lives in `unity/Assets/Scripts/Sim/` under the `HGO.Sim` assembly
  definition (`noEngineReferences: true`, C# 9): Core (`Cfg`, `U`/`Rng`, `Input`), Combat
  (`Weapons`, `Hitscan`), Entities (`Actor`, `Player`, `Bot`), World (maps, movement, grapple,
  nav, layout), Sim (`Simulation`, `Effects`, `HeadlessRunner` including `ScriptedPlay`), and the
  engine-free `ViewModelSpring` (namespace `HoodGlobalOffensive.Render`, folder `Sim/Render/`).
  Namespaces and member names mirror the reference so the two can be diffed; the mechanical
  conversion rules are:
  - file-scoped namespaces become braced, body indented (Unity compiles C# 9)
  - explicit `using System;` / `System.Collections.Generic;` / `System.Linq;` (no implicit usings)
  - `Raylib_cs.Color` fields become packed ARGB `uint` via `U.Argb` (`U.Rgb`/`U.Rgba` renamed)
  - raylib-only members (`ToRaylib`) are dropped; no BCL `PriorityQueue` — `NavGraph` carries a
    hand-rolled 4-ary min-heap that reproduces the BCL dequeue order for equal priorities
- **Verification (`tools/parity`):** a plain .NET console project that compiles the Unity sim
  sources directly (LangVersion 9, no engine), checks hundreds of golden values captured from the
  reference (main @ `6a6fbdd`, v0.2.2), then runs **both CI gates** through the ported
  `HeadlessRunner` and byte-compares the reports — the wall-clock line is the only value excluded.
  Must end in `PARITY: PASS`:

  ```powershell
  & $dotnet build tools\parity\Parity.csproj --nologo -warnaserror
  & $dotnet run --project tools\parity\Parity.csproj --no-build
  ```

  Golden reports live in `tools/parity/goldens/`. Refresh them only when the reference's headless
  output changes deliberately: run the two CI-gate commands, keep everything from the first
  `====` separator, and strip the ` wall clock` line.
- **Shared suite + Unity edit-mode gate:** the checks live once, in
  `unity/Assets/Tests/EditMode/ParityChecks.cs` — engine- and framework-free, collecting result
  lines into a `ParityReport`. `tools/parity/Program.cs` is a thin console wrapper over it, and
  the edit-mode assembly `HGO.Sim.Tests` (`ParityTests.cs` + `HGO.Sim.Tests.asmdef`) runs the
  same suite from Unity's Test Runner against the same `tools/parity/goldens/` files. Headless
  invocation once the editor exists:

  ```powershell
  Unity -batchmode -projectPath unity -runTests -testPlatform EditMode -testResults results.xml
  ```

  Both test files were compile-checked and dry-run outside the editor (stub NUnit + UnityEngine,
  C# 9, `-warnaserror`) and the console harness passes, but no real Unity editor has compiled
  them yet — the first editor open is still the next milestone. Commit the `.meta` files Unity
  generates for `unity/Assets/Tests/EditMode/`. The compile check is now a committed, repeatable
  gate: `tools/editmode-check` (see below).
- **Presentation bridge done:** `unity/Assets/Scripts/Presentation/MatchDriver.cs` drives
  `Simulation.Update(InputFrame, dt)` at a fixed 1/60s accumulator, adapts legacy Unity input
  (mouse, keyboard axes, one-shot flags) into `InputFrame`, and syncs `PlayerBody` (feet position
  + yaw) and `PlayerCamera` (eye-height position + yaw/pitch aim) to `Sim.Player`. It lives in
  `HGO.Presentation.asmdef` which references `HGO.Sim` (engine-free, `noEngineReferences: true`).
  The adapter negates Unity's `"Mouse Y"` before feeding `InputFrame.Look`: the sim consumes raw
  raylib `GetMouseDelta` counts (Y positive = down, `src/Core/Input.cs:82`) and subtracts them
  (`Player.cs:122`), whereas Unity's Mouse Y is positive = up — without the negation the ported
  view looks inverted. The bridge is now **compile-verified editor-free** by
  `tools/presentation-check` (a clean `-warnaserror` build there proves `MatchDriver.cs` still
  type-checks against `HGO.Sim`'s public API through a stub engine — it does not run the bridge).
  **Not yet ported:** rendering/map meshes, HUD/menus, settings, audio playback, screenshot
  tooling, bot view prefabs. The Unity project itself is a skeleton: editor version
  `6000.3.26f1` (`unity/ProjectSettings/ProjectVersion.txt`), a minimal package manifest (test
  framework + uGUI), the `HGO.Sim` asmdef and the new `HGO.Presentation` asmdef. **No Unity
  editor pass has been run yet** — opening the project in Unity 6.3 and running the edit-mode
  parity tests (already authored, see above) is the next milestone.
  **Gates verified on this box (2026-10-17):** once SDK 10.0.402 was installed user-locally, all
  three gates passed locally — `dotnet build --nologo -warnaserror` (0 warn/0 err), both CI
  gates `RESULT: PASS`, and `tools/parity` `PARITY: PASS` (Unity port byte-matches the raylib
  reference goldens on both runs; the `unknown map 'nope'` line it prints is the harness's
  fallback-map test, not an error). The remaining unverified gate is Unity's Test Runner —
  editor only.

## How to work on this repository

### Build, run, verify

If the SDK is installed per-user, call it explicitly. On the current machine (`F:\` work box)
`C:\Program Files\dotnet\dotnet.exe` is a runtime-only host and **SDK 10.0.402 was installed
user-locally** at `C:\Users\Aryan\AppData\Local\Temp\opencode\dotnet` (via `dotnet-install.ps1`;
that temp path may be wiped, so re-run the installer with `-Channel 10.0` if it's gone). Use:

```powershell
$dotnet = "$env:LOCALAPPDATA\Temp\opencode\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "C:\Users\Aryan\AppData\Local\Temp\opencode\dotnet\dotnet.exe" }
$env:DOTNET_ROOT = Split-Path $dotnet
& $dotnet build --nologo -warnaserror        # must stay 0 warnings, 0 errors
& $dotnet run --no-build -- <args>
```

The two CI gates (also run by `.github/workflows/verify.yml` on every push) must print
`RESULT: PASS`:

```bash
dotnet run -- --headless --seconds 300 --bots 9  --skill Normal --seed 20260114
dotnet run -- --headless --seconds 60  --bots 15 --skill Hard   --seed 7331
```

The Unity sim port has its own gate — it compiles `unity/Assets/Scripts/Sim/**` and checks it
against golden captures of the reference, then re-runs both CI gates through the ported runner:

```bash
dotnet build tools/parity/Parity.csproj --nologo -warnaserror
dotnet run   --project tools/parity/Parity.csproj --no-build   # must end in PARITY: PASS
```

The presentation layer has its own gate — `tools/presentation-check` compiles the engine-free sim
sources **plus** `unity/Assets/Scripts/Presentation/**` against a minimal UnityEngine stub
(`UnityStubs.cs`) mirroring the Unity assembly layout (`HGO.Presentation` → `HGO.Sim`), so
`MatchDriver.cs` cannot rot against the sim's public API without a real editor. The clean
`-warnaserror` build is the gate; the exe prints `PRESENTATION: PASS` for CI logs:

```bash
dotnet build tools/presentation-check/PresentationCheck.csproj --nologo -warnaserror
dotnet run   --project tools/presentation-check/PresentationCheck.csproj --no-build
```

The edit-mode test assembly has its own gate too — `tools/editmode-check` compiles the
engine-free sim sources **plus** `unity/Assets/Tests/EditMode/**` against minimal
NUnit.Framework + UnityEngine stubs (`UnityTestStubs.cs`), mirroring the `HGO.Sim.Tests`
assembly layout (references `HGO.Sim`, `UnityEngine.TestRunner`, `nunit.framework.dll`; editor
platform only; `UNITY_INCLUDE_TESTS`). It proves the parity tests still compile against the
sim's public API without an editor — actually *running* them still needs the Unity Test
Runner. The exe prints `EDITMODE: PASS`:

```bash
dotnet build tools/editmode-check/EditModeCheck.csproj --nologo -warnaserror
dotnet run   --project tools/editmode-check/EditModeCheck.csproj --no-build
```

### Capturing screenshots (the precision workflow)

```bash
dotnet run -- --screenshot shot.png --weapon Rifle --seconds 4 --bots 9 --width 1920 --height 1080
dotnet run -- --screenshot ads.png  --weapon Rifle --zoom            # ADS pose
dotnet run -- --screenshot clip.png --weapon SMG   --anim RELOAD1:1.0 # pinned action pose
dotnet run -- --screenshot cab.png  --map Arena    --screen upgrade   # staged UI screens
```

- `--anim <action[:t]>` pins a viewmodel clip (DRAW | IDLE | INSPEC | RELOAD1 | RELOAD2 | SHOOT)
  at a clip time — deterministic, invaluable for verifying poses.
- **Capture determinism gotcha:** the capture fires at the first qualifying tick, so
  `--seconds 4` and `--seconds 9` can produce the exact *same* frame. To get a different scene,
  change `--map` or `--seed` (or both).
- **Judging alignment:** never eyeball at 1×. Crop + upscale with
  `System.Drawing` (`NearestNeighbor`, 3×) into `.assets-scratch/` before reading the image.
- Captures need a real GPU; headless machines cannot run `--screenshot`.

### Audio workflow

```bash
dotnet run -- --sfx-dump sfx     # writes all 47 generated cues as WAVs
```

There is no audio device in `--headless`, so this is how you audition or regression-check cues.
Quick sanity check — peaks should sit roughly 0.2–0.8, never 0 (silence) or pegged at 1.0:

```powershell
# peak/RMS over 16-bit PCM: skip 44-byte header, read int16s, |v|/32767
```

### Asset pipeline (rig viewmodels)

- Sources are staged under `.assets-scratch/` (gitignored, local-only) — see
  `assets/weapons/viewmodel-rig-SOURCE.md` for origin + SHA-256 of each drop.
- Conversion: `tools/build_animated_viewmodels.py`, run with Blender:
  `F:\WORK\cli-gui\.tooling\blender-5.2.2\blender-5.2.2-windows-x64\blender.exe --factory-startup --disable-autoexec --background --python tools/build_animated_viewmodels.py -- --scratch <dir> --out assets\weapons`
- Outputs are committed (`assets/weapons/viewmodel-{rifle,sniper,smg,pistol}.glb`); the rigs use
  a 49-bone mixamo-style skeleton, glTF +Z muzzle / +Y up, drawn with a 180° yaw. Placement is
  trigger-anchored: `pivot = blended target − Turn(scale · TriggerModel)`. Full details in
  `assets/weapons/viewmodel-rig-README.md` (the old `ak47-viewmodel-README.md` is the fallback
  doc).

### Push policy

Push when the team asks or when a release / verified milestone is ready — **the pre-push gate is
the verified state above:** `-warnaserror` build clean, both CI gates `RESULT: PASS`,
`tools/parity` `PARITY: PASS`, `tools/presentation-check` build clean, and
`tools/editmode-check` build clean. When you push, update
the "Push policy" bullet under "Where the project stands" with the new `origin/main` sync point so
the next session knows whether GitHub is current.

### Release ritual

1. Bump the version in **three** places: `Cfg.Title` and `Cfg.Version` (`src/Core/Cfg.cs`) and
   `<Version>` (`HoodGlobalOffensive.csproj`).
2. Add a `CHANGELOG.md` entry (Keep-a-Changelog style).
3. Run the build + both CI gates. Capture a fresh menu/HUD sanity screenshot if UI changed.
4. Publish:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true \
     -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
     -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o release/win-x64
   ```
5. Verify the exe **from a clean directory** (copy it elsewhere, run `--headless --seconds 30`;
   assets are embedded in the single file).
6. Commit, tag (`v0.2.x`), `git push origin main --follow-tags`, then
   `gh release create v0.2.x release/win-x64/HoodGlobalOffensive.exe --title ... --notes-file ...`
   (or `gh release upload v0.2.x <file> --clobber` to replace an asset on an existing release).

## Code map (where to look for what)

| Area | File |
| --- | --- |
| Rig runtime: clip sampling, ADS blend, calibrated `AdsTargets`, `ForceAction` dev override | `src/Render/AnimatedViewmodels.cs` |
| Draw order, rig-first weapon draw, `IronSightOffsetFor`, sniper stow, camera-anchored sky (`DrawProceduralSky`, `SkyClouds`) | `src/Render/Renderer.cs` |
| Screenshot tooling: `--weapon` / `--zoom` / `--reticle` / `--anim` | `src/Render/ScreenshotCapture.cs` |
| All sound synthesis (`Biquad`, gunshot layers, hitmarker, reload…) | `src/Audio/WavGen.cs` |
| Weapon family table shared with the dump, override resolution | `src/Audio/AudioBank.cs` |
| `--sfx-dump` writer | `src/Audio/AudioAudit.cs` + `Program.cs` |
| Menus (see the row-pitch note above) | `src/UI/Screens.cs` |
| Rig converter | `tools/build_animated_viewmodels.py` |
| Unity sim port (mirrors `src/`, engine-free) | `unity/Assets/Scripts/Sim/` |
| Sim→Unity bridge: fixed-tick `Simulation.Update`, `InputFrame` adapter, body/camera sync | `unity/Assets/Scripts/Presentation/MatchDriver.cs` + `HGO.Presentation.asmdef` |
| Parity suite (shared) + Unity edit-mode tests | `unity/Assets/Tests/EditMode/` (`ParityChecks.cs`, `ParityTests.cs`, `HGO.Sim.Tests.asmdef`) |
| Parity console harness + golden reports | `tools/parity/` (`Parity.csproj`, thin `Program.cs`, `goldens/`) |
| Presentation compile gate (editor-free) | `tools/presentation-check/` (`PresentationCheck.csproj`, `UnityStubs.cs`, thin `Program.cs`) |
| Edit-mode test compile gate (editor-free) | `tools/editmode-check/` (`EditModeCheck.csproj`, `UnityTestStubs.cs`, thin `Program.cs`) |
| Unity project skeleton | `unity/` (`ProjectVersion.txt`, `Packages/manifest.json`, `HGO.Sim.asmdef`) |
| Existing docs | `docs/GUIDE.md`, `docs/BUILDING.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md` |

## Gotchas learned the hard way

- **`shots/` PNGs are `.gitignore`d by design** (ad-hoc QA previews stay out). README-bound
  captures must be `git add -f`-ed after copying them in with their final numbered name.
- **`.assets-scratch/` is local scratch** — captures, crops, SFX dumps, staged source assets.
  Never referenced by the build; safe to delete.
- **`--seconds` does not vary the captured frame** (see determinism note above); vary `--map` or
  `--seed` instead.
- **Headless has no GPU and no audio device** — `--screenshot` and audio playback only work on a
  real machine, everything else works everywhere.
- **The exe ships as `WinExe`** (GUI subsystem) so double-clicking is clean; CLI modes print via
  inherited handles with an `AttachConsole` fallback in `Program.cs`. Don't "fix" the missing
  console — it's deliberate.
- **The Unity port must stay C# 9** — file-scoped namespaces, `record struct` and other C# 10+
  syntax are out; the parity project pins `LangVersion 9.0` so the compiler catches drift.
- **Never hand-edit `tools/parity/goldens/`** — they are captures of the reference; regenerate
  them from the CI-gate runs as described in the migration-state section.
- **`ParityChecks.cs` is compiled by two projects** — the console harness (C# 9, no engine) and
  Unity's `HGO.Sim.Tests`. Keep it free of engine, NUnit and C# 10+ syntax; after changing it run
  `tools/parity` and, once the editor exists, the edit-mode tests. Unity generates `.meta` files
  for `unity/Assets/Tests/EditMode/` on first open — commit them with the next editor change.
- **`tools/presentation-check` is a compile gate, not a runtime** — `UnityStubs.cs` models only the
  Unity members the presentation layer uses, so adding a new Unity API call to `MatchDriver.cs`
  (or new presentation files) means extending the stub. `CS8618` is suppressed there because Unity
  wires `[Header]` fields in the inspector and does not enable nullable; keep every other warning
  an error. Run the gate after any change under `unity/Assets/Scripts/Presentation/`.
- A menu row added to the main menu will re-break the footer overlap unless the pitch math above
  is re-checked.

## Open items / sensible next steps

- **Unity migration, next milestones** (in order):
  1. Open `unity/` in Unity `6000.3.26f1`, let it generate `.meta` files / `Library/` (both are
     gitignored), and confirm `HGO.Sim` compiles in the editor with no engine references.
  2. Run the authored edit-mode parity tests (`unity/Assets/Tests/EditMode/`, assembly
     `HGO.Sim.Tests`) in the editor's Test Runner — they already reuse `tools/parity/goldens/`;
     until the editor is part of CI the console harness remains the automated gate. Commit the
     `.meta` files Unity generates.
  3. Wire the Unity scene: empty `PlayerBody` GameObject with `MatchDriver`, separate unparented
     `Main Camera` assigned to `MatchDriver.PlayerCamera` (the driver sets its world pose). Tune
     `LookGain` so mouse counts match the raylib feel (Unity default axes are not 1:1 counts).
  4. Rebuild presentation (rendering/map meshes, HUD, menus, audio) against the reference —
     gameplay behaviour is already pinned by the parity goldens, so the view layer is free to
     diverge where it is purely cosmetic. Every presentation change goes through
     `tools/presentation-check` (build clean) and, when a new Unity API is touched, through an
     update to `UnityStubs.cs`.
- **`tools/editmode-check` is the same idea for the test assembly** — `UnityTestStubs.cs` models
  only `NUnit.Framework` (`[Test]`, `Assert.Fail`, `TestContext.Progress`) and
  `UnityEngine.Application.dataPath`, so extending the edit-mode tests with new NUnit or Unity
  APIs means extending the stub. Note `dataPath` returns the literal `"Assets"` (relative),
  which is fine for a compile-only gate; the real path only matters when tests run in the
  editor.
- **Bacon-hand assets** (`hands/source/baconhand_smg.fbx`) were reviewed and deliberately left
  unused — the rigs already ship gloved arms. Swap them in only if the look is wanted.
- **Sniper ADS** does not exist (model stows, scope overlay takes over — verified clean). Adding
  a true scoped ADS pose is possible but was out of scope.
- **Cloud polish** (optional): the three high wisps at 23–26° elevation currently render like the
  lower banks; more distinct cirrus variant if the sky gets another pass.
- **Audio tuning by ear** — the cue set is mechanically verified (no silence, no clipping) but
  was never listened to on hardware by the development session; `--sfx-dump` + a real playback
  device is the way to A/B it.
- Feature direction lives in `docs/ROADMAP.md`.

## Provenance reminder

The Industrial Zone map and the animated weapon rigs are **owner-supplied third-party assets**
with their own records (`assets/maps/LOW-POLY-INDUSTRIAL-ZONE-ATTRIBUTION.md`,
`assets/weapons/viewmodel-rig-SOURCE.md`). They are excluded from the MIT licence; keep the
records with any distribution or fork.
