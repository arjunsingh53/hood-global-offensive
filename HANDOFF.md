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
  handoff-per-task rule (`3ebec29`), the push policy (`a5ebde6`), the shared parity suite with
  Unity edit-mode tests (`affecef`), the sim-to-Unity presentation bridge and its compile gate
  (`ad97fb2`, `f3d51e8`), the edit-mode compile gate (`14144b3`), the procedural audio synthesis
  port (`8ebbce1`), the `AudioMix` routing port, the audio playback bridge
  (`unity/Assets/Scripts/Presentation/SimAudioPlayer.cs`), the first presentation-blockout pass
  (`MapBlockout` etc.), the code-built front end (`MenuSync`), the Unity YAML `.gitattributes`
  prep, the combat-effects blockout (`EffectsSync`), the HUD floating damage numbers, the
  crosshair options port (settings profile, live HUD reticle, menu screen), the
  camera-feedback levels (driver FOV kick + view-model offsets), the main-menu match-setup
  rows (loadout slots, map, bots, skill, kill limit), and the upgrade-cabinet overlay. The
  GitHub release asset for `v0.2.2` was
  rebuilt in place (`gh release upload --clobber`) so the downloadable exe includes the menu fix.
  Run `git log --oneline -5` for exact hashes.
- **Verified state:** `-warnaserror` build clean; both headless CI gates print `RESULT: PASS`;
  the parity harness prints `PARITY: PASS` (including byte-exact replays of both gates through the
  ported runner, all 47 synthesised cues byte-matching the reference `--sfx-dump` by SHA-256, and
  21 reference-captured `AudioMix` routing goldens — local-scope bypass, distance/pan boundaries,
  trauma ducking, Arena occlusion rays); the presentation compile gate
  (`tools/presentation-check`) builds clean; the single-file exe was re-verified from a clean
  directory.
- **Push policy:** `origin/main` is synced with `main` through `acd6652` ("Record the migration
  sync push in the handoff", 2026-10-16, at team request so the other machine could pull the sim
  port and the edit-mode tests). The follow-up migration commits (presentation bridge and its
  compile gate, edit-mode gate, audio synthesis port, `AudioMix` routing port, audio playback
  bridge, first presentation-blockout pass, the code-built front end, the Unity YAML
  `.gitattributes` prep, combat-effects blockout, the HUD floating damage numbers, the crosshair
  options port, the camera-feedback levels, the main-menu match-setup rows, the upgrade-cabinet
  overlay, the Tab scoreboard + pause/match-over parity fixes, the scenic menu backdrop, the
  scope/hook/vitals HUD pass, the top-bar + compass pass, the rotating radar scanner, the weapon-readout card) are
  **local only — 21 commits ahead of `origin/main`** — keep them local until the team asks
  for another push.
  Pre-push gate for future work: `-warnaserror` build clean, both CI gates `RESULT: PASS`, `tools/parity`
  `PARITY: PASS`, `tools/presentation-check` build clean, and `tools/editmode-check` build clean.
  After a push, update this bullet with the new sync point.
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
- **Procedural audio ported (engine-free):** `unity/Assets/Scripts/Sim/Audio/` carries the whole
  synthesised cue bank — `WavGen.cs` (RBJ-biquad filter-shaped noise; mechanical port of
  `src/Audio/WavGen.cs`) and `AudioCatalog.cs` (the weapon-family table plus the 47-cue manifest,
  mirroring the reference `AudioBank.WeaponFamilies` + `AudioAudit.Dump` order and parameters).
  It produces the same in-memory 16-bit PCM WAV bytes the raylib reference writes with
  `--sfx-dump`, so the Unity presentation layer only has to wrap the bytes in an `AudioClip` —
  no audio device, editor or `.NET` asset file is involved in the synthesis itself. Byte-parity
  is enforced by the shared suite against `tools/parity/goldens/audio-hashes.txt` (one SHA-256
  per cue, captured from the reference dump; regenerate only from a reference `--sfx-dump` run
  when a cue's recipe changes deliberately). **Playback is now ported too:** the presentation
  layer's `SimAudioPlayer` (see the bridge bullet below) wraps these PCM buffers in `AudioClip`s
  and drains the sim's sound events — the wrap/build-voices step is compile-gated editor-free, the
  actual output only needs a running device.
- **AudioMix routing ported (engine-free):** `unity/Assets/Scripts/Sim/Audio/AudioMix.cs` is the
  mechanical port of `src/Audio/AudioMix.cs` (namespace `HoodGlobalOffensive.Audio`):
  `SoundSpectrumProfile` (FullRange/Distant/Occluded), the `SoundPlayback` result struct, the
  static `Resolve` (local-scope bypass; distance attenuation to `MaxAudibleDistance` 2600;
  listener-space pan via `RightFromYaw`; Distant gain 0.82, Occluded 0.50 with narrowed pan,
  both with clamped pan lerps) and `IsOccluded` (segment ray against the map's `RayHit` with a
  0.75-unit endpoint tolerance, never self-occluding), plus critical-health trauma ducking via
  `Cfg.LowHealthWorldSoundFloor`. Verified by 21 reference-captured routing goldens in the
  shared suite (`CheckAudioMix` in `ParityChecks.cs` — 5 assertions per case), so `tools/parity`
  and the Unity edit-mode tests both cover it; the negative test (tamper one golden) printed the
  exact mismatch and `PARITY: FAIL (1 mismatches)`, then was reverted.
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
  **Audio playback ported:** `unity/Assets/Scripts/Presentation/SimAudioPlayer.cs` is a
  `MonoBehaviour` that, on `Awake`, wraps every `AudioCatalog.Cues()` WAV (canonical 44-byte-header
  mono PCM16 — header parsed and validated, resampled to `AudioClip.Create` + `SetData`) in an
  `AudioClip`, keyed by the runtime cue name the sim emits (`rifle_ak-distant.wav` → `rifle_ak.distant`,
  `step-floor.wav` → `step.floor`). It owns a round-robin pool of `AudioSource` voices (replacing the
  reference's 3-voices-per-cue) and exposes `Play(name, volume, pitch, pan)` plus
  `PlaySimulationSounds(Simulation)` — the latter resolves each `SoundEvent` through
  `AudioMix.Resolve` (listener position/yaw, map occlusion, trauma), applies the same
  0.012 volume floor and 0.3–3 pitch / 0–1 pan clamps, and the same distant/occluded/`step` cue
  fallback as the reference `AudioBank`. `MatchDriver` owns one on its own GameObject (auto-created
  if the inspector field is empty) and drains `Sim.Sounds` after **each** fixed sim step, because the
  sim clears its sound list at the start of the next `Update`. Compile-verified editor-free by
  `tools/presentation-check`; actual output needs a device.
  **First presentation pass ported (blockout quality):** five more `MonoBehaviour`s live next to
  the bridge, all compile-verified editor-free by the same gate:
  - `MapBlockout.cs` — in `Start` builds one scaled, brush-tinted cube per `Sim.Map.Brushes`
    entry under an auto-created root (walkable world without a mesh pass); re-checks in
    `LateUpdate`, so a restart that swaps maps clears the old cubes and rebuilds.
  - `ActorViews.cs` — in `LateUpdate` keeps one yaw-facing capsule per non-player actor
    (red/cyan by team, removed on death); the local player is skipped.
  - `ViewModelSync.cs` — ports the camera-local offset/rotation composition of the reference
    `Renderer.DrawViewModel` (hip pose table, sway, bob, land dip, reload/deploy dip, view kick,
    both view-model springs, hip yaw with the iron-sight lerp and the scope stow rule) onto a
    camera-child `ViewModelRoot`. Stows for magnified optics at `ZoomBlend > 0.70`. The mesh
    itself is assigned in the editor; per-art offsets are not ported (see the gotcha about
    rotation-sign verification).
  - `HudSync.cs` — code-built uGUI blockout of the reference match HUD: health card
    (value/label/segment fill), ammo card (weapon, mag `00`, reserve `INF`), match timer,
    score line, kill feed (6 lines, 6.5 s life), the settings-driven centre crosshair,
    hitmarker (0.12 s life, kill/headshot tint), damage flash + rotating directional arrow,
    low-health trauma vignette,
    respawn notice and the match-result banner. Floating damage numbers are now ported too: the
    sim's player-dealt damage numbers are projected from their world hit point through the
    assigned camera (`WorldToViewportPoint`) and positioned in 1080p canvas units, so the
    reference's y-down screen flip lives there; the reference fade curve `min(1, t*2.2)`, larger
    amber `!`-suffixed headshot style and its half-alpha drop shadow are kept. Uses
    `UnityEngine.UI` (uGUI is already in the
    package manifest; TextMeshPro is NOT — don't add a TMP HUD here casually).
    **Crosshair ported:** the reticle is now the ported `CrosshairSettings` profile
    (`CrosshairSettings.cs` in the presentation folder — model, presets, `CrosshairPreference`,
    and the uGUI `CrosshairGraphic` blockout of the reference `CrosshairRenderer`). The HUD
    reticle follows the reference rules exactly: it hides while dead or behind a real scope /
    calibrated sight picture (`ZoomBlend > 0.75`), and its dynamic gap is the weapon's
    `SpreadFor(…)` projected through the camera's vertical FOV at 1080p, clamped to 0.32×height.
    The profile instance is pushed by `MenuSync` (public `HudSync.Crosshair` property, same
    object so edits show live); without a menu the default Classic profile applies.
    **Scoreboard ported (Tab):** holding Tab during play raises the reference
    `Screens.DrawScoreboardContent` panel (1140-wide at 1.5x on the 1080p canvas): map header,
    "FIRST TO" line, K/D/K-D/PTS columns from `Simulation.Leaderboard()`, the gold row behind
    the local player, and the "TAB to close" footer. It never appears while paused (the driver
    is disabled outside Playing). While the match-over screen is up, `MenuSync` sets
    `HudSync.ForceScoreboard` so the same board shows there like the reference — one documented
    divergence: the Unity board sits under the menu backdrop instead of above it.
    **Centre-zone pass ported:** the cosmetic scope overlay (radius-540 circular mask, cross
    lines, 40-segment inner ring, centre dot; one `CanvasGroup` fades the whole picture over
    `ZoomBlend` 0.25→1; mask hole approximated with 36 strips per side that never over-mask
    into the circle), hook status squares (Q/E light gold while the hook is attached), the
    station prompt (`[ E ]  UPGRADE CABINET` + live kill-points line while in range), the
    vitals build row (HP/DMG/SPD/JMP pips with live levels from `Simulation.UpgradeLevel`)
    and the COMBAT READY / CRITICAL status word, plus the reference peripheral fade
    (`Hud.PeripheralHudOpacity`: peripheral anchors drop to 0.38 opacity behind a real scope
    or calibrated sight picture; reticle, hitmarker and scope mask never fade).
    **Top bar + compass ported:** the top-centre match-clock panel (MATCH CLOCK caption,
    two-digit clock that turns red under 30 s like the reference, YOU/LEAD/FT line from
    `Simulation.Leaderboard()`; the panel border tints with the accent/urgency colour since
    the arena corner brackets are not drawn) and the heading tape (630-wide at 1.5x, ticks
    every 15 degrees with N/NE/... labels at 45s, gold caret, three-digit heading under the
    strip; game yaw flipped to clockwise compass degrees exactly like the reference).
    **Radar ported:** `RadarGraphic` (a `MaskableGraphic` building the scanner as a uGUI
    mesh in the reference's logical units with the 1.5x scale applied at emit) draws the
    player-centred rotating scanner — backdrop circles, grid rings/cross, every non-floor
    brush edge as a yaw-rotated segment clipped to the circle, always-on gold cabinet dots,
    contact dots (bright when in LOS and under 1100 m) that degrade to edge chevrons past
    radius-7, the up-facing player triangle with boundary rings — over the square
    arena-panel border/fill images, with FIELD SCANNER / RANGE 72 labels. Still
    blockout: the arena-panel card skin (corner
    brackets), and the peripheral-fringe/trauma-mark vignette extras.
    **Weapon readout ported:** the bottom-right card is now the ported
    `Hud.DrawWeaponReadout` instead of the ad-hoc name/mag/reserve texts —
    weapon name + state line (`MELEE - V` / `ALT - DOUBLE BLAST` / `SCOPE - RMB` /
    `IRON SIGHTS - RMB` / `PRIMARY`), the oversized `00` magazine numeral with
    `/` + `INF`, `MAG`/`RES` captions, the eight-segment magazine bar inside its
    1 px outline, the reload progress bar + `TACTICAL`/`EMPTY RELOAD` line (or the
    blinking `PRESS R` prompt), and the four loadout slot boxes above the card
    (active slot gold + left rail, `---` for an empty slot, `ShortName` labels).
    Built in the reference's 1280x720 logical units at the 1.5x canvas scale, so
    the knife box overhangs the screen edge exactly like the reference. The
    card/slots reuse the fill + 1 px border approximation (corner brackets still
    owed by the skin pass).
  - `MenuSync.cs` — code-built uGUI front end and game-flow owner (main / pause / settings /
    crosshair options / match-over). It never changes sim rules: leaving the Playing screen
    disables the
    `MatchDriver` (stopping the fixed tick), unlocks the cursor and zeroes `Time.timeScale`;
    entering it re-enables the driver, locks the cursor and restores time. `PLAY`/`RESTART
    MATCH`/`PLAY AGAIN` call `MatchDriver.Restart()` (fresh `Simulation` built from the menu's match
    setup); pause `QUIT TO MENU` and match-over `BACK TO MENU` return to the main screen like
    the reference (`ScreenAction.QuitToMenu`), while only the main menu's QUIT exits the app.
    The pause screen carries the reference's live kills/deaths/upgrade-points subtitle and the
    match-over subtitle appends the reference's "you finished rank X of Y" line. Entering the
    main screen parks the camera on the reference's scenic menu backdrop
    (`Renderer.DrawMenuBackdrop`'s fixed 58° pose over the Industrial raw coordinates, via
    `MatchDriver.ParkMenuBackdrop`); every other screen keeps the live gameplay frame behind
    it, like the reference. Also owns the
    cursor — the driver still locks it in its `Awake`, but `MenuSync` applies menu state in
    `Start` (after every `Awake`), so the main menu wins. Keyboard-driven (Up/Down to move,
    Enter to select, Esc to pause/back, Left/Right to adjust) so it needs no
    `EventSystem`/`Button` wiring. Settings: look sensitivity (`MatchDriver.LookGain`) and
    invert-Y (`MatchDriver.InvertLookY`), persisted via `PlayerPrefs`. **Crosshair options
    screen ported:** reached from main and pause, nine rows (preset / colour / dynamic gap /
    centre dot / base gap / arm length / thickness / reset / back) with the reference's
    semantics — presets restore full profiles, any manual tweak flips the label to CUSTOM,
    bounds match the reference (gap 0–24, length 1–24, thickness 1–5), and a live preview
    panel shows the reticle at scale 1.75 with a 5 px test spread.     Persistence is PlayerPrefs
    (`HGO.Crosshair.*` keys, -1 preset = custom) with the reference `UserSettingsStore` restore
    rules: unknown enums fall back, values clamp, and a stored preset survives only if every
    value still matches it. **Camera feedback ported:** OFF / LOW / NORMAL (0 / 0.45 / 1.0),
    carried inline on the main and pause menus like the reference and persisted as
    `HGO.CameraFeedback.Level`. The level scales both render-only feedback layers: the FOV
    lens kick in `MatchDriver.SyncView` (reference `GetFeedbackFovKick` — never a positional
    shake, so the centre ray is identical at every strength; the HUD crosshair spread gap
    reads the same field of view) and the viewmodel-local footstep/landing/damage offsets in
    `ViewModelSync` (reference `GetFeedbackOffset`, ADS-damped by `FeedbackAdsScale`).
    **Match setup ported:** the main screen doubles as the reference match-setup list — SLOT 1-3
    (independent pool cycling via `WeaponRegistry.ByName`/`CycleLoadout`), MAP (through
    `MapRegistry.Names`), BOTS (1..`Cfg.MaxBots`), BOT SKILL (Easy..Expert), KILL LIMIT (5..100
    in fives) — twelve tighter rows with the same Left/Right semantics. Values persist as
    `HGO.Match.*` PlayerPrefs and are pushed onto `MatchDriver` (`BotCount`/`Skill`/`KillsToWin`/
    `MapName` + the three loadout slots) for the next `Restart`; restore rules mirror the
    reference `UserSettingsStore` (clamps, unknown skill → Normal, map `Normalize`, gun slots
    sanitised against the loadout pool).
  - `UpgradeSync.cs` — the kill-point upgrade cabinet overlay (reference `Screens.UpgradeMenu`).
    When the sim raises `Player.WantsOpenUpgrade` (E next to a station), it opens a code-built
    uGUI panel and freezes the match — `MatchDriver.Update` skips its tick while `IsOpen`,
    exactly like the reference never steps the sim from its UpgradeMenu state. Four stat rows
    with three level pips each, KILL POINTS badge, live COST/MAX/NEEDS POINTS status, then a
    CLOSE row and a 1.6 s result message; Up/Down (or W/S) move, Space/E buys via
    `Simulation.TryBuyUpgrade`, Esc closes, and the E that opened the cabinet must be released
    once before it can buy (reference `UpgradeMenuKeyLatch`). The two deviations from the
    reference, both documented in the file: the CLOSE row is drawn like a normal row (the
    reference leaves it invisible) and the footer key hint sits below it for 1080p legibility.
    `MenuSync` skips its own input while `UpgradeSync.BlocksMenuInput` (open, or the frame it
    closed), so the closing Esc never also pauses.
  `MatchDriver.SyncView` also now pins `PlayerCamera.fieldOfView` to `Sim.Player.CurrentFov`.
  **Combat-effects blockout ported:** `EffectsSync.cs` pools primitive stand-ins for the sim's
  effect lists, synced in `LateUpdate` (the sim owns every lifetime — it ages and prunes the
  lists inside `Simulation.Update`, so this view only reads): tracers as stretched cubes along
  `From`→`To` with the reference's 0.08 minimum width; impact debris as cubes and blood mist as
  spheres using the reference's life-driven size curves; blood droplets as cubes; surface/blood
  decals as cubes flattened along the dominant normal axis exactly like `Renderer.DrawDecalList`.
  Pools grow lazily to the `Cfg` caps (240 particles, 72 mists, 96 droplets, 112 impact decals,
  80 blood splats). `ViewModelSync` also gained the blockout muzzle flash: a white-hot sphere at
  the reference's box-weapon bore estimate (`(-1, bore, -Forward + 2)`, added to its pose table),
  radius pulsing with `Player.MuzzleFlash` over its 0.045 s life, suppressed for melee and hidden
  with the model. Opaque primitive materials cannot fade like the reference's alpha draws, so the
  scale curves carry the life cue and tracers/decals simply vanish when pruned; the amber flame
  lobes wait for the mesh pass. Compile-verified editor-free by `tools/presentation-check`.
  Wiring: attach `MenuSync`/`HudSync`/`ActorViews`/`MapBlockout`/`ViewModelSync`/`EffectsSync`/`UpgradeSync` to the same
  GameObject as `MatchDriver` (they self-locate it) and assign `ViewModelRoot` (a camera-local
  placeholder mesh) to `ViewModelSync`; everything else auto-builds. `MenuSync` is optional —
  without it the driver runs unpaused from frame one with the cursor locked, as before;
  `UpgradeSync` is optional too, and without it the cabinet simply never opens.
  **Not yet ported:** real map meshes and bot meshes (primitive blockouts exist), VFX polish
  (transparent/additive materials, muzzle flame lobes), screenshot tooling, per-art view-model
  calibration. The menu stack is a
  first pass (`MenuSync`): main/pause/settings/crosshair-options/match-over are covered; an
  audio/keybind options pass and a styled reference-matched skin are
  still blockout quality. The
  Unity project itself is a skeleton: editor version
  `6000.3.26f1` (`unity/ProjectSettings/ProjectVersion.txt`), a minimal package manifest (test
  framework + uGUI), the `HGO.Sim` asmdef and the new `HGO.Presentation` asmdef. **No Unity
  editor pass has been run yet** — opening the project in Unity 6.3 and running the edit-mode
  parity tests (already authored, see above) is the next milestone.
  **Gates verified on this box (2026-10-17):** once SDK 10.0.402 was installed user-locally, all
  gates passed locally — `dotnet build --nologo -warnaserror` (0 warn/0 err), both CI gates
  `RESULT: PASS`, `tools/parity` `PARITY: PASS` (Unity port byte-matches the raylib
  reference goldens on both runs; the `unknown map 'nope'` line it prints is the harness's
  fallback-map test, not an error), and `tools/presentation-check` / `tools/editmode-check`
  print `PRESENTATION: PASS` / `EDITMODE: PASS`. The audio checks are part of `tools/parity`:
  synthesis prints `ok audio 47 cues byte-match reference --sfx-dump`, and the `AudioMix`
  routing battery (21 reference-captured cases) runs silently on success; both went through
  negative tests (tamper one golden hash / one routing golden) and correctly failed. The
  remaining unverified gate is Unity's Test Runner — editor only.

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
(`UnityStubs.cs`, which now also stands in for `Object`/`GameObject`/`AudioClip`/`AudioSource`)
mirroring the Unity assembly layout (`HGO.Presentation` → `HGO.Sim`), so `MatchDriver.cs` and
`SimAudioPlayer.cs` cannot rot against the sim's public API without a real editor. The clean
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
| Unity audio synthesis + mix routing port, cue manifest (engine-free) | `unity/Assets/Scripts/Sim/Audio/` (`WavGen.cs`, `AudioCatalog.cs`, `AudioMix.cs`) |
| Sim→Unity bridge: fixed-tick `Simulation.Update`, `InputFrame` adapter, body/camera sync | `unity/Assets/Scripts/Presentation/MatchDriver.cs` + `HGO.Presentation.asmdef` |
| Unity audio playback: PCM→`AudioClip` bank, voice pool, drains `Sim.Sounds` via `AudioMix.Resolve` | `unity/Assets/Scripts/Presentation/SimAudioPlayer.cs` |
| Unity blockout pass: brush cubes, bot capsules, sim-driven view-model + muzzle flash, code-built uGUI HUD (incl. Tab scoreboard + mesh radar) + menu stack, pooled combat effects, crosshair profile + reticle, camera-feedback levels, match-setup rows, upgrade-cabinet overlay | `unity/Assets/Scripts/Presentation/` (`MapBlockout.cs`, `ActorViews.cs`, `ViewModelSync.cs`, `HudSync.cs`, `RadarGraphic.cs`, `MenuSync.cs`, `EffectsSync.cs`, `CrosshairSettings.cs`, `CameraFeedbackSettings.cs`, `UpgradeSync.cs`) |
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
- **`audio-hashes.txt` is the one golden that hashes instead of holding text** — one
  `name sha256` line per cue, captured by running the reference `--sfx-dump <dir>` and hashing
  each WAV. The parity suite regenerates the cues through the ported `AudioCatalog` and compares
  hashes (the WAVs are ~1.6 MB, so bytes are not committed).
- **`ParityChecks.cs` is compiled by two projects** — the console harness (C# 9, no engine) and
  Unity's `HGO.Sim.Tests`. Keep it free of engine, NUnit and C# 10+ syntax; after changing it run
  `tools/parity` and, once the editor exists, the edit-mode tests. Unity generates `.meta` files
  for `unity/Assets/Tests/EditMode/` on first open — commit them with the next editor change.
- **Unity YAML assets are pinned to LF in `.gitattributes`** (`*.meta`, `*.unity`, `*.prefab`,
  `*.asset`, `*.mat`, `*.anim`, `*.controller`, `*.asmdef` are `text eol=lf`) so a Windows
  checkout never surfaces phantom-dirty scene or meta files; add any new Unity text extension to
  that list.
- **`tools/presentation-check` is a compile gate, not a runtime** — `UnityStubs.cs` models only the
  Unity members the presentation layer uses (`Object`/`GameObject`/`Component`, `AudioClip`,
  `AudioSource`, plus the transform/input/math types), so adding a new Unity API call to
  `MatchDriver.cs` or `SimAudioPlayer.cs` (or new presentation files) means extending the stub.
  `CS8618` is suppressed there because Unity wires `[Header]` fields in the inspector and does not
  enable nullable; keep every other warning an error. Run the gate after any change under
  `unity/Assets/Scripts/Presentation/`.
- A menu row added to the main menu will re-break the footer overlap unless the pitch math above
  is re-checked. `MenuSync` mirrors that split: the main (12 rows) and crosshair (9 rows) screens
  run tight 42-px rows under the lifted title, the shorter screens keep the roomy 52-px pitch.
- **`UnityStubs.cs` now models the uGUI slice too** (Color/Material/Renderer/Camera/RectTransform/
  Canvas/CanvasScaler/Text/Image/TextAnchor/Resource fonts...). It is compile-only: getters return
  stubs and layout does nothing, so HudSync's rect math cannot be regression-tested here — verify
  placement visually at the first editor run (it targets a 1920×1080 CanvasScaler reference).
- **`ViewModelSync` rotation signs are unverified** — raylib rotates right-handed, Unity
  left-handed; each flourish/spring Euler component was negated port-side. If the first editor
  capture shows the flourish leaning the wrong way, flip the sign on the offending
  `Quaternion.Euler` axis in `FlourishRotation`, not the sim.
- **`ActorViews`/`MapBlockout`/`HudSync` attach to the `MatchDriver` GameObject** — they resolve it
  in `Awake` / `Start` (`MapBlockout` waits for `Start` because `MatchDriver.Awake` creates the
  Simulation). `enabled` self-disables with a warning if the driver is missing.
- **`MenuSync` owns game flow, cursor and `Time.timeScale`** — it disables the `MatchDriver`
  component (not `gameObject`) outside the Playing screen, so the fixed tick, input capture and
  audio drain all stop while a menu is up. The driver still locks the cursor in its own `Awake`,
  but `MenuSync` applies menu state in `Start` and wins; don't move that to `Awake` (component
  `Awake` order on one GameObject is undefined). The match-setup rows push bot count / skill /
  kill limit / map / loadout onto the driver, and PLAY / RESTART read them back in
  `MatchDriver.Restart()`. `MapBlockout` re-checks `Sim.Map` every `LateUpdate`, so swapping the
  map clears the old cubes and rebuilds even mid-Play-Mode.

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
      Add `MenuSync` to the same GameObject for the main/pause/settings front end (optional: the
      driver runs unpaused with the cursor locked without it). Audio needs no wiring:
      `SimAudioPlayer` is auto-added to the `MatchDriver` GameObject and the
      `Main Camera` already carries Unity's default `AudioListener`.
  4. Rebuild presentation (rendering/map meshes, HUD, menus) against the
     reference — gameplay behaviour is already pinned by the parity goldens, so the view layer is
     free to diverge where it is purely cosmetic. A first presentation pass now exists as a
      blockout (`MapBlockout` brush cubes, `ActorViews` capsule bots, `ViewModelSync` view-model
      dynamics + muzzle flash, `HudSync` code-built uGUI HUD, `MenuSync` code-built menu stack
      (incl. the ported crosshair options screen and match-setup rows), `UpgradeSync` cabinet
      overlay, `EffectsSync` tracers/particles/decals,
      `CrosshairSettings` profile + reticle graphic, `CameraFeedbackSettings`) — see the migration-state bullet; editor
      wiring is one GameObject + one viewmodel placeholder. What remains above blockout: real map
      meshes and bot meshes, VFX polish (transparent/additive materials, muzzle flame lobes),
      a styled options/settings screen, screenshot
      tooling, per-art viewmodel calibration.
      Audio synthesis, `AudioMix` routing and the
      `SimAudioPlayer` playback bridge are already ported (`Sim/Audio/` + `Presentation/`), so audio
      is wired end to end bar a device to hear it. Every presentation change goes through
      `tools/presentation-check`
      (build clean) and, when a new Unity API is touched, through an update to `UnityStubs.cs`.
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
