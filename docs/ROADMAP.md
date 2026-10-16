# Roadmap

Where HOODMATCH is going, in rough order. Nothing here is committed to a date — this is an early
access project built in the open, and priorities follow what breaks the game most.

---

## v0.2.x — tighten what exists

Correctness and polish on the shipped systems rather than new features.

### 1. Balance pass against the reference

The weapon table is authored from Redmatch 2's published numbers, but feel is not a spec sheet.
Repeated headless runs across all four bot skills on every map to check time-to-kill, hook
recovery and upgrade pacing, then adjust `WeaponDef` and `Cfg` numbers with measurements in hand.

### 2. Cabinet placement authority

Cabinets currently auto-place from the nav graph. A per-map authored list (plus the auto fallback)
would let layouts tune cabinet positions deliberately — one awkward cabinet can define a whole
map's mid-game.

### 3. Key rebinding

The controls are hardcoded. The settings file (schema v2) already persists preferences; a bindings
screen that writes into it is the next step.

---

## v0.3.0 — depth

### In-game settings screen

Sensitivity, FOV, volume and crosshair in one place instead of menu rows, `F2` and the pause menu
split. Needs its own deliberate UI pass rather than an opaque config dump beside the EXE.

### A sixth and seventh map

The box-arena pipeline makes maps cheap: an `Add(...)` list in `MapRegistry`, automatic pathing,
spawn validation and cabinets. The next maps should be built *around* the hook — vertical shafts,
long sightlines for slingshots, tight interiors where hooks are a liability.

### Spectate and practice mode

A no-bots arena with infinite points to lab hook routes, and a follow-camera for finished matches.
Both are presentation-layer work on top of the deterministic sim.

---

## Later / exploratory

- **A replay / demo system.** The simulation is deterministic given a seed and an input sequence,
  which means a full match can be recorded as a seed plus inputs. Nearly free given the existing
  `InputFrame` abstraction, and it would make bug reports reproducible.
- **Better bot hook usage.** Bots navigate but rarely gank through the air the way the movement
  system invites. Teaching bots to slingshot would change how a match reads.
- **Networking.** The simulation/presentation split means the simulation is already
  headless-capable and deterministic. A server-authoritative model with client-side prediction is
  plausible, but it is a large project and it depends on everything above being stable.
- **Team modes.** Team deathmatch first, since perception and combat are already team-agnostic;
  objective modes only after that.

---

## Explicitly not planned

- **A level editor.** The map is code. That is a deliberate choice, and keeping it that way keeps
  the navigation graph derivable and the collision model trivial.
- **A different renderer or a move to a game engine.** The point of this project is that it is
  written from scratch on a thin binding, and it builds clean with zero warnings and no engine.
- **Unreviewed or unlicensed asset packs.** The project only integrates supplied assets with a
  source/provenance record. The optional sky panorama slot is intentionally empty by default.
