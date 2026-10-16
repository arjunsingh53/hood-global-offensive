# Development handoff

> **Read this first if you are picking the project up (human or AI session).** It describes the
> exact state of the repository, the workflows that are easy to get wrong, and what is still open.
> Everything here is reproducible from a clean clone of `main`.

## Where the project stands

- **Release:** `0.2.2`, tagged `v0.2.2`, published on the
  [releases page](https://github.com/arjunsingh53/hood-global-offensive/releases) with a
  self-contained Windows x64 `HoodGlobalOffensive.exe` (assets embedded, no .NET install needed).
- **`main` = the v0.2.2 work plus one follow-up commit** (`9e782f9`): the README screenshot
  gallery, the main menu layout fix, this handoff and `AGENTS.md`. The GitHub release asset for
  `v0.2.2` was then rebuilt in place (`gh release upload --clobber`) so the downloadable exe
  includes the menu fix. Run `git log --oneline -5` for exact hashes.
- **Verified state:** `-warnaserror` build clean; both headless CI gates print `RESULT: PASS`;
  the single-file exe was re-verified from a clean directory.
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

## How to work on this repository

### Build, run, verify

If the SDK is installed per-user (typical on the original machine), call it explicitly:

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet build --nologo -warnaserror        # must stay 0 warnings, 0 errors
& $dotnet run --no-build -- <args>
```

The two CI gates (also run by `.github/workflows/verify.yml` on every push) must print
`RESULT: PASS`:

```bash
dotnet run -- --headless --seconds 300 --bots 9  --skill Normal --seed 20260114
dotnet run -- --headless --seconds 60  --bots 15 --skill Hard   --seed 7331
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
- A menu row added to the main menu will re-break the footer overlap unless the pitch math above
  is re-checked.

## Open items / sensible next steps

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
