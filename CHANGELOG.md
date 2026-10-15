# Changelog

All notable changes to Hood Global Offensive are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project aims
to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the version is `0.x`,
the minor number may contain breaking changes.

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
- The "Crossfire" arena: a 2400 × 2400 deathmatch map with a central building, a west corridor with
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
defined per weapon but not applied, armour is granted free on every respawn (making the kevlar
purchase redundant), there is no stomach hitbox, sound does not occlude, there are no grenades or
networking, there is one map and one mode, and settings do not persist between launches.

---

[0.1.0]: https://github.com/arjunsingh53/hood-global-offensive/releases/tag/v0.1.0
