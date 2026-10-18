# Changelog

All notable changes to HOODMATCH (formerly Hood Global Offensive) are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims
to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is `0.x`,
the minor number may contain breaking changes.

---

## [Unreleased]

Start of the complete migration to Unity: the deterministic simulation is ported into the Unity
project and byte-verified against this reference implementation by a parity harness.

### Added

- **Unity sim port** (`unity/Assets/Scripts/Sim/`, assembly `HGO.Sim` with
  `noEngineReferences: true`): the whole engine-free simulation — `Cfg`, `U`/`Rng`, `InputFrame`,
  weapons and hitscan, `Actor`/`Player`/`Bot`, maps, character movement, grapple, nav graph,
  effect records, the `Simulation` match orchestrator and the headless runner with its scripted
  robot. The port mirrors the reference namespaces and members, is C# 9-compatible for Unity's
  compiler profile, turns raylib colours into packed ARGB `uint`s (`U.Argb`) and replaces the
  BCL `PriorityQueue` with a heap that reproduces its dequeue ordering.
- **Parity harness** (`tools/parity/`): compiles the Unity sim sources with a plain .NET SDK and
  checks hundreds of golden values captured from the reference — RNG streams, aim math, weapon
  spread/recoil/reload, falloff curves, map brushes and spawns, movement and grapple traces,
  hitscan results and nav-graph paths — then runs both headless CI gates through the ported
  runner and byte-compares the reports (wall-clock excepted). Golden reports are checked in
  under `tools/parity/goldens/`.
- **Shared parity suite with Unity edit-mode tests** (`unity/Assets/Tests/EditMode/`): the checks
  now live once, in an engine- and framework-free `ParityChecks.cs`; the console harness is a
  thin wrapper over it, and the new `HGO.Sim.Tests` edit-mode assembly runs the same suite from
  Unity's Test Runner against the same checked-in goldens — so the port can be gated from inside
  the editor as soon as the project is first opened there.

### Changed

- **Bot world models**: the world-model bots now present the owner-supplied *Tactical Soldier 02*
  operator — a Character-Creator rig with HGO-authored locomotion loops and an integrated
  low-ready carbine — instead of the HGO capsule combatant. The capsule combatant stays the
  automatic fallback (used whenever the owner-supplied file is absent) and remains the fully
  game-owned option; the flat block-proxy weapon is now drawn only for the legacy fallback.
  Presentation only — no simulation, weapon, port or golden surface changes.

### Fixed

- **Scoped sniper shots missed the reticle**: scoping multiplied the sniper's 5.5° idle cone by
  0.25, leaving ~1.4° of scatter (about ±80 px at 18° zoom) on a still, fully-scoped shot, so
  the rifle never looked aligned. A planted shot now resolves through the crosshair (~0.11°),
  ground movement feeds a quadratic stability penalty into the magnified sight picture, and the
  view punch drops from 8° to 2.5° with a faster recovery (0.28 s delay, 30°/s) so the rifle
  keeps its weight without throwing the view. Mirrored in the Unity sim port for parity.
- **Scope picture rebuilt** (`Hud.DrawScopeMask` / `Hud.DrawScopeReticle`, and `HudSync`): the
  flat circle-and-cross became a duplex reticle — heavy posts, an open-centre fine crosshair, a
  mil-dot drop ladder, a soft rim shadow and the exact centre pixel the hitscan ray resolves
  through. The corner mask now draws before the peripheral HUD, so the corner anchors fade over
  the tunnel instead of ghosting through it.
- **Impact decals floated in the sky**: marks left on the Industrial Zone's collision-only
  containment rim hung in mid-air for their full 12-second life. Collision-only surfaces now
  carry `Brush.Invisible` and reject decals while keeping the spark and sound as boundary
  feedback; decals on the floor and structures are unchanged.
- **Screenshot staging tightened**: `--screenshot`'s "enemy in view" gate now requires the enemy
  near frame centre, so captures stop staging against a wall that fills the view.
- **Sky-ramp banding** (`src/Render/Renderer.cs`): the dusk gradient was drawn as 56 flat bands
  (~22 px each at 1080p) and its steps read as faint horizontal lines through the main menu's
  darkened backdrop scrim. The ramp now uses 160 steps (~8 px), which presents as a smooth
  gradient at 1080p.

---

## [0.2.2] — 2026-10-16

Weapon-presentation and audio pass: the four primary weapons now use supplied animated rigs, the
firearm cues are rebuilt from shaped noise instead of saturated tones, and the procedural sky is
anchored to the camera.

### Added

- **Rig viewmodels** (`src/Render/AnimatedViewmodels.cs`): the rifle, sniper, SMG and pistol now
  render four supplied animated rigs (`assets/weapons/viewmodel-{rifle,sniper,smg,pistol}.glb`,
  mixamo-style skeletons with gun bones) instead of the legacy static meshes. Hip placement and
  ADS alignment were calibrated per weapon from captured frames; every clip — draw, idle, inspect,
  both reloads and shoot — plays from the rig itself, with the SMG and pistol reloads animated.
  `assets/weapons/viewmodel-rig-README.md` documents the conversion pipeline and
  `viewmodel-rig-SOURCE.md` records the origin and SHA-256 of every source drop; the legacy
  `ak47-viewmodel-README.md` remains the fallback reference.
- **`--anim <action[:t]>`** screenshot flag: pins a viewmodel action (DRAW | IDLE | INSPEC |
  RELOAD1 | RELOAD2 | SHOOT) at a clip time so any pose can be captured deterministically.
- **`--sfx-dump <dir>`**: writes every generated sound cue to WAV files for auditioning without
  launching the game; the firearm table is shared with the live bank so the two cannot drift.

### Changed

- **Firearm audio rebuilt** (`src/Audio/WavGen.cs`): the synthesised reports no longer saturate
  pure sine tones. Each layer is filter-shaped noise through an RBJ biquad chain — a muzzle snap
  around 2 kHz over a midrange bark, a resonant low body with a filtered sub, a pressure tail whose
  low-pass sweeps darker over the decay, a struck-metal mechanical clack, and a delayed first
  reflection. Distant and occluded profiles reduce the snap and drop the mechanical layer on
  purpose.
- **Hitmarker, headshot, impact, flesh hit and reload cues rebuilt** on the same basis: clean
  two- and three-partial confirmations with no saturation buzz (a 1245/1868 Hz pair and a
  1440/2160/3240 Hz stagger), a thud-chip-ring wall impact, a low thud plus wet tick for flesh,
  and four resonant metallic reload clicks.
- **Procedural sky rebuilt** (`src/Render/Renderer.cs`): the backdrop is camera-anchored — the
  horizon, sun and clouds all track pitch and yaw with correct parallax — and the clouds are
  layered cumulus banks with shaded undersides, lit tops and a backlit tint near the sun, rather
  than screen-glued blobs.

### Fixed

- **Heartbeat cue was never audible**: its envelope early-out ran at sample zero, so the whole
  buffer was silent; the early-out now tests the decay term only.
- **Main menu overflowed its footer hints**: once the 0.2.0 rows (kill limit, sensitivity,
  crosshair, camera feedback) were added, the twelve rows ran into the controls hint block and
  clipped the QUIT row. The row pitch and start were tightened so the list clears the hints on
  every resolution.

---

## [0.2.1] — 2026-10-16

Reference-parity pass against Redmatch 2's published base stats and patch notes.

### Changed

- **Rifle damage 25 → 30**, matching the reference's 1.41.4 default (nerfed there from 34).
- **SMG damage 16 → 20**, matching the reference's 1.41.3 buff.
- **Revolver**: 75 damage, a shot every 0.4 s, 0.4 s pull-out, 2.0 s reload, and no spread of any
  kind — the reference gives it 100% accuracy at all times.
- **Shotgun**: 9 pellets × 17 damage (153 on a direct hit) and a linear falloff band from 20 m to
  70 m ending at 0.1x, matching the falloff the reference added alongside the Double Barrel.
- **Double Barrel falloff** is now continuous — 1x until 15 m, declining to 0.2x at 30 m — instead
  of three discrete tiers, and the alt-fire blast launches victims at 380 u/s (its 9.5 m/s stat).
- **Grounded WASD movement no longer adds bullet spread**; only airborne movement carries spread
  penalties, mirroring the reference's 1.41.3 movement-spread removal.
- **Pull-out times**: the SMG, Revolver and Double Barrel take 0.4 s to raise after a switch, with a
  matching view-model raise; their published values are the only ones the reference documents, so
  every other gun still raises instantly.

---

## [0.2.0] — HOODMATCH

The Counter-Strike-era identity is retired. The game is now a Redmatch-2-style arena shooter:
bunny-hopping, dual grappling hooks, flat-damage guns and kill-point cabinet upgrades. There is
no Counter-Strike mode left — no buy menu, no money, no armour, no spray patterns, no grenades.

### Added

- **Dual grappling hooks** (`src/World/Grapple.cs`): `Q` fires the left hook, `E` the right. Rope
  pull scales with length, walls can be climbed, both hooks can hold at once, and roofed slingshot
  routes reach the 45 m/s movement ceiling. `E` beside a cabinet resolves to interaction instead.
- **Kill-point upgrade cabinets**: kills award points, points buy permanent per-match stat levels
  (Health +25 / Damage +12% / Speed +6% / Jump +7%, 3 levels each) at wall cabinets, and death drops
  one level from the highest-upgraded stat. Placement is automatic per map from the nav graph.
- **Five-map catalog** (`src/World/MapRegistry.cs`): Industrial (the supplied GLB) plus four box
  arenas — Arena, Streets, Ruins, Playground — rendered directly from their collision brushes.
  `--maps` lists them; `--map <name>` selects one for the game, headless runs and screenshots.
- **Spawn sanitization**: every map validates its authored spawn list against its own collision at
  load time and relocates any spawn that lands inside geometry, deterministically.
- **Double Barrel secondary fire**: right-click spends a shell as a 270-damage point-blank blast
  with heavy self-knockback, doubling as a movement tool.
- **Quick knife (`V`)**, flashlight (`F`, presentation only), hook status squares on the HUD, and a
  cabinet interaction prompt.

### Changed

- **Movement rebuilt** around the reference's rules: a single fast run speed, hold-`Space`
  auto-hop where only grounded `WASD` input kills momentum, and grapple physics as the skill
  ceiling. Crouch remains (`Ctrl`/`C`) as a mechanical trade, not a tactic.
- **Weapons rebuilt** (`src/Combat/Weapons.cs`): seven guns (Pistol 25, Revolver 50, SMG 16 auto,
  Rifle 25 auto, Shotgun 15×8, Sniper 100 scoped, Double Barrel 90 with range tiers) plus the
  knife. Damage is uniform — no headshot multiplier, no armour; only the Double Barrel has range
  falloff. All reserves are infinite. Recoil kicks after an on-crosshair shot; spread is
  deterministic from movement/air/bloom terms.
- **HUD rebuilt** around health, build pips, kill points, weapon slots and hook state; scoreboard
  tracks points; the buy menu, money and armour readouts are gone.
- **Rebrand**: window title, menus and console output now say HOODMATCH v0.2.0.
- **Settings schema v2**: map and loadout selections persist alongside the existing crosshair,
  camera-feedback, sensitivity and match preferences. v1 documents are upgraded in place.
- **`BotProfile` tiers, `Actor`, `Simulation` and `Effects`** updated for upgrades, kill points,
  hooks and cabinets; the soft body push between actors now re-checks level collision, so it can
  never shove an actor into a wall.

### Removed

- Six source verifier harnesses (`MovementVerifier`, `WeaponReadinessVerifier`, the settings,
  audio, combat-feedback and gameplay-reach checks) and every flag that ran them; the headless
  harness now covers movement, grappling, upgrades and settings round-trips directly.
- The Python/bash screenshot verification tooling (`capture_shots.sh`, `verify_shots.py`,
  `sweep_weapons.py`, `inspect_png.py`), the standalone gallery page, the committed root
  executable and the old handoff document.

### Verification

- The headless report now asserts grapple rope limits, spawn clearance on any map, and cabinet
  existence, runs on a `-warnaserror` build, and executes the scripted robot's hook, quick-knife,
  flashlight and upgrade purchases. CI runs a 300-second 9-bot Normal match and a 60-second
  15-bot Hard match, both with fixed seeds.

---

## [0.1.1 worktree] — Hood Global Offensive

The application identified itself as version `0.1.1`. This section records the old worktree's
changes only; it does **not** assert that a distributable v0.1.1 executable, GitHub Release, or
asset-rights review was completed. Superseded by 0.2.0.

### Added

- **The Glock 17 is drawn from a real mesh** (`assets/weapons/glock17.glb`) instead of from boxes. It
  is the only binary file in the repository: 17,546 triangles and 1024/512/256 PNG textures, 2.7 MB,
  generated from a 145 MB source model (1,169,840 triangles, 8K albedo) by the new checked-in
  pipeline [`tools/build_weapon_asset.py`](tools/build_weapon_asset.py). The pose — scale, centre and
  the bore line the muzzle flash sits on — is measured from the loaded mesh, so re-exporting the
  asset does not silently break the framing.
- `src/Render/WeaponModels.cs`: mesh loading, measurement and drawing, with a graceful per-weapon
  fallback to the box shape if a mesh is missing or unloadable.

### Changed

- The default sidearm is named **Glock 17** (was `Glock-18`), matching the mesh that draws it.
- `WeaponRegistry.ByName` no longer silently substitutes the Glock for an unknown name; it reports the
  substitution on stderr, and the lookup is now case-insensitive. `--weapon Deagle` had been quietly
  equipping a Glock — only `Desert Eagle` resolves — which is how `shots/02-deagle.png` came to show a
  pistol that was not a Desert Eagle. `tools/capture_shots.sh` is fixed, and the shot is regenerated.
- The framing checks (`tools/inspect_png.py`, `tools/verify_shots.py`, `tools/sweep_weapons.py`) now
  recognise an art-mesh weapon by shape — dark, near-neutral pixels — rather than only by the box
  weapons' exact colours, and they stop short of the HUD's radar panel, which is a similar grey. The
  threshold sits below the world's darkest flat surface (a metal brush skirt at `(75, 81, 88)`); a
  control frame with the view model suppressed measures 0.0% inside the checked regions.
- `shots/06-glock17.png` added, and every screenshot artifact regenerated.

### Fixed

- **The reload flourish swung the Glock 17 almost entirely out of frame.** The dip and tilt were
  tuned for the chunky box weapons, and a real pistol is slim enough that the full angle took it
  below the screen: the weapon sweep measured 1.0% coverage of the checked lower-right region
  mid-reload against 9.3% while idle. Art weapons now apply a per-weapon share of the flourish
  (`ReloadSwing`), which brings the reloading frames back to 5.1%.

### Active implementation work

#### Fixed

- **The Buy Station no longer flashes closed when `B` opens it.** The opening key is now latched
  until it has been released, so only a later fresh `B` press closes the overlay; `Esc` remains
  immediately available.
- Full standard-issue Kevlar + Helmet now reads `EQUIPPED` and cannot deduct $1,000 for a no-op.
  Damaged or incomplete protection still restores to full and deducts exactly the listed price.
- Corrected the supplied Industrial Zone GLB's pavement origin, structure collision tops, and five
  unreliable spawn anchors. Actor feet, collision and visible service roads now share the same
  gameplay plane.
- Air steering now preserves, but cannot create, horizontal speed. This removes the previous
  repeat-jump/strafe speed gain while retaining deliberate in-air direction control.

#### Added

- Hold-to-aim for all non-scoped firearms; the AWP preserves its click-to-toggle optic. The original
  AK view model has a rebuilt matte-black front post, front channel and rear U-notch, plus an ADS-only
  compact sight pose that preserves the authoritative shot ray. Weapons without a calibrated sight
  picture keep their reticle visible while aiming instead of hiding it behind an unusable fallback model.
- Refined the original AK's front/rear sight silhouette, reduced its exposed charging handle and
  tightened its ADS footprint so the player sees a clearer open-notched aim picture without importing
  a third-party weapon model.
- A responsive, two-column Buy Station with affordability feedback, large clickable cards and
  quick-buy keys.
- Local player-preference persistence for bot setup, sensitivity, crosshair and camera feedback.
  The versioned `%LOCALAPPDATA%` document is bounded, atomically written, and tested against malformed,
  oversized and unsupported-schema input without serializing match or combat state.
- An original low-poly **HGO Capsule Combatant** replaces Tactical Soldier 02 as the active world
  character. It has `Idle`, `Walk`, `Run`, `Crouch` and `Prone` presentation clips, a compact
  generic world carbine, and deterministic accessory silhouettes; it does not replace the Gun Game
  first-person weapons or simulation hitboxes.
- Player prone (`Z`) and a restricted, collision-validated 0.60-second mantle over low static cover.
  Mantling preserves aim but temporarily blocks firing, reload and ADS.
- A world-only dusk grade, original procedural sky, HUD/weapon separation, layered tracers and
  additive muzzle flashes. A licensed 2:1 equirectangular panorama can be supplied locally as an
  optional override; no third-party sky source is bundled or downloaded by the project.
- Critical-health and hit feedback: a peripheral trauma/heartbeat layer, world-sound ducking,
  actor-hit mist/droplets, short-lived blood splats and bounded impact/decal pools.
- Map-aware procedural audio routing: original four-layer gunshot fallbacks, distance profiles,
  static-geometry occlusion profiles, stereo pan, and floor/crate footstep variants.

#### Changed

- Replaced stale Crossfire-era local screenshot artifacts with Industrial Zone, AK ADS, Buy Station
  and HGO Capsule Combatant captures. The screenshot regeneration and visual checks now cover that
  working set.
- Menus and overlays now use a centered 1280 × 720 logical canvas with inverse-mapped mouse input,
  preserving clickable cards and hierarchy from the supported 960 × 540 minimum through ultrawide
  windows. The combat HUD stays in physical framebuffer pixels.
- The title screen now uses the supplied Industrial Zone and original dusk treatment as a restrained
  live backdrop; an authorised local panorama is used only when present and valid. Characters use a
  ground-level contact shadow rather than an opaque rectangular shadow slab.

---

## [0.1.0] — 2026-10-15

The first public early access release. Everything below is new.

### Added

**Movement**
- Quake/CS-derived character controller: ground friction, run acceleration, a 30 unit/s per-tick air
  acceleration cap (strafe-jumping works), 800 unit/s² gravity and a 300 unit/s jump.
- Swept AABB collision with automatic step-up onto ledges up to 20 units.
- Crouching that shrinks the hitbox and refuses to stand up under a low ceiling.
- Walk mode for quiet movement.

**Gunplay**
- Hitscan weapons with separate head, body and leg hitboxes.
- Per-weapon damage falloff, expressed as an exponential decay per 1000 world units.
- A full armour model: 50% of damage reaches health through kevlar, 100% for a headshot against a
  target with no helmet.
- Cover penetration: thin geometry marked penetrable lets reduced-damage bullets through, with
  higher penetration power on the rifles, the Desert Eagle and the AWP.
- Eight weapons with distinct statistics: knife, Glock-18, Desert Eagle, MP5-SD, AK-47, M4A4, Nova
  and AWP.
- Authored 30-entry per-weapon recoil spray patterns. The first bullet always flies true; recoil is
  applied after the shot, kicks the real view, and must be compensated manually. Patterns recover on
  their own after a short delay and reset when you stop firing or reload.
- Spread that grows with movement speed, airborne state and per-shot bloom, and shrinks by 38% when
  crouched, visualised live by the crosshair gap.
- The AWP scope with a 20° field of view.

**Bots**
- Perception through a ~65° view cone plus a close-range hearing radius, every element gated by a
  real line-of-sight raycast.
- A* pathfinding over a 895-node navigation graph sampled automatically from the map geometry.
- Duel behaviour: strafing, holding preferred engagement ranges, taking cover when hurt, firing in
  bursts with pauses, and compensating for their own accumulated recoil.
- Four difficulty tiers (Easy, Normal, Hard, Expert) varying reaction time, turn rate, aim error,
  view range, engagement range and burst discipline.

**World**
- The "Crossfire" arena (later superseded at runtime by Industrial Zone in the v0.1.1 worktree): a
  2400 × 2400 deathmatch map with a central building, a west corridor with
  a single doorway, an east raised terrace reached by stepped blocks, four full-height corner
  pillars, a broken north sightline and a south crate pit.
- 13 spawn points with selection that keeps them at least 900 units from living players and verifies
  they are clear of geometry.

**Economy and match rules**
- Starting money, per-kill rewards, a $16,000 cap, headshot bonuses, and notably large knife, Nova
  and smaller AWP kill rewards.
- A buy menu covering weapons and kevlar, with loadouts persisting through respawns.
- Free-for-all deathmatch: 30 kills to win, a 10-minute time limit, a 3-second respawn delay and a
  money floor on respawn so you can never be locked out of a usable weapon.

**Interface**
- Full first-person HUD: spread-reactive crosshair, health, armour and helmet, ammo, money, a colour
  kill feed with headshot markers, hitmarkers, floating damage numbers, directional damage arcs, and
  a low-health pulse.
- A radar that reveals only enemies the player currently has line of sight to.
- A held-`Tab` scoreboard with kills, deaths, accuracy and headshot counts for every actor.
- Main menu with bot count, bot skill, kill limit and mouse sensitivity; pause, buy and match-over
  screens.

**Audio**
- Every sound effect synthesised to WAV at startup, with distance attenuation. The repository
  contains no binary audio assets.

**Tooling and verification**
- `--headless` mode: plays a complete match with a scripted player, no window and no GPU, and
  asserts invariants including embedded-in-geometry ticks, non-finite transforms, kills matching
  deaths, spawn clearance, and that the match actually produced shots and kills.
- `--screenshot` mode: runs a match, waits for a frame worth capturing, and renders one genuine
  gameplay frame to a PNG, including menu, buy menu, scoreboard and scoped states.
- `tools/inspect_png.py`, a dependency-free PNG decoder.
- `tools/verify_shots.py`, which measures view-model framing, the scope mask and overlay rendering
  on the real shipped artifacts, and includes a positive control so the stowed-weapon check is
  provably capable of detecting a weapon.
- `tools/sweep_weapons.py`, which checks all eight view models at five different moments each.
- `tools/capture_shots.sh`, which regenerates every screenshot artifact reproducibly.
- `play.cmd`, a Windows launcher that sets up the environment and keeps the console open on failure.

### Fixed during development

Recorded here because they are the reason the verification tooling exists:

- `Actor.Deaths` was never incremented, which would have left every scoreboard showing zero deaths
  permanently. Caught by the headless harness.
- The view model rendered as a thin sliver with its muzzle sitting on top of the crosshair. Caught by
  the PNG measurement check.
- The reload animation rotated the anchor frame, swinging the weapon **up** into view instead of
  dipping it down. Caught by the weapon sweep.
- The view model was occluded by level geometry, dropping from roughly 10,000 visible pixels to about
  100 when the player stood near a wall. Fixed by rendering the weapon into its own render target.
  (`Rlgl.DisableDepthTest()` was tried first and does not work through raylib's batched renderer.)
- A stowed weapon left a ghost of its previous frame composited over the scope. Fixed with a
  drawn-this-frame flag.

### Known issues

See [the README](README.md#known-issues) for the current list. In summary: armour penetration is
defined per weapon but not applied, armour remains free on respawn (though the Buy Station now avoids
charging twice), and there is no stomach hitbox. World sound now selects original distant and
static-map-occluded profiles, but it has no material-aware or dynamic-actor occlusion. There are no
grenades or networking, and there is one map and one mode. Local player preferences now persist;
key bindings, display and audio settings remain future work.

---

[0.1.0]: https://github.com/arjunsingh53/hood-global-offensive/releases/tag/v0.1.0
