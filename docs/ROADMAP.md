# Roadmap

Where Hood Global Offensive is going, in rough order. Nothing here is committed to a date — this is
an early access project built in the open, and priorities follow what breaks the game most.

---

## v0.2.0 — fix what the code already promises

These are correctness issues rather than new features. Each one is a place where the code, the
weapon table, or the UI implies something that does not actually happen yet.

### 1. Wire up armour penetration

`WeaponDef.ArmorPenetration` is defined for every weapon (0.47 for the Glock-18, 0.775 for the AK-47,
0.975 for the AWP) and **nothing reads it**. Armour currently applies a flat rule: 50% of damage
reaches health, 100% if the target has no helmet and you hit the head.

The fix is to use the per-weapon ratio when splitting damage between health and armour, so that a
high-penetration weapon genuinely punishes armour and a low-penetration weapon genuinely does not.
This is the largest gameplay difference between the current build and Counter-Strike, and the
weapon table already contains the data to fix it.

### 2. Stop giving away armour for free

Every respawn currently sets armour to 100 and the helmet flag to true, which makes the buy menu's
$1000 "Kevlar + Helmet" item dead money and halves the value of body shots for everyone, always.

Options: make armour a purchase and remove the respawn grant, or keep a cheaper floor so new players
are not defenceless. Either way the economy needs a decision rather than an accident.

### 3. A stomach hitbox

`HitGroup.Stomach` exists with a 1.25× multiplier, but no hitbox is ever assigned to it — body
damage is uniform across the torso. Splitting the torso into chest and stomach would make the
existing multiplier live.

### 4. Interface and sensitivity persistence

The main menu's settings reset every launch. A small config file (sensitivity, bot count, bot skill,
kill limit, resolution, volume) written next to the executable would fix it.

---

## v0.3.0 — depth

### Grenades

The single biggest missing piece of Counter-Strike's tactical vocabulary. High explosive and flash
first: both are straightforward extensions of the existing raycast and effect systems, and both give
players a way to break a bot's line of sight or clear the west doorway.

### A second map

The architecture already supports it — a map is a list of `Add(...)` calls and the navigation graph
derives itself from the geometry. A smaller, tighter map would also make the shotgun and SMG feel
like real choices rather than niche picks.

### Key rebinding

The controls are hardcoded. A bindings screen that writes to the same config file as above.

### A functional in-game settings screen

Resolution, FOV and volume are currently command-line or menu-only. FOV in particular is a personal
preference that deserves a slider.

---

## v0.4.0 — modes

### Team deathmatch

Two teams, friendly fire off, shared score. This mostly touches scoring and spawn selection, since
perception and combat are already team-agnostic.

### Bomb defusal

The mode people actually think of when they think of Counter-Strike: two sites, a plant, a defuse
timer, round-based economy with win/loss bonuses, and a half-time swap. This is a significant piece
of work — round state machine, site volumes, purchase phases, spectator handling on death — and it
depends on armour and economy being settled first.

---

## Later / exploratory

- **Sound occlusion.** Gunfire currently transmits through walls with only distance attenuation.
  Raycasting the audio path would make the map's geometry readable by ear.
- **Bot buying.** Bots are handed loadouts by tier; letting them buy from their own money would make
  the economy meaningful on both sides.
- **A replay / demo system.** The simulation is deterministic given a seed and an input sequence,
  which means a full match can be recorded as a seed plus inputs. This is nearly free to build given
  the existing `InputFrame` abstraction, and it would make bug reports reproducible.
- **Networking.** The simulation/presentation split means the simulation is already headless-capable
  and deterministic. A server-authoritative model with client-side prediction is plausible, but it
  is a large project and it depends on everything above being stable.
- **Better bot coordination.** Bots currently act independently. Even light role assignment — one
  pushes, one holds — would change how a match reads.

---

## Explicitly not planned

- **A level editor.** The map is code. That is a deliberate choice, and keeping it that way keeps the
  navigation graph derivable and the collision model trivial.
- **A different renderer or a move to a game engine.** The point of this project is that it is written
  from scratch on a thin binding, and it currently builds clean with zero warnings and no engine.
- **Asset packs or licensed content.** The repository contains no binary assets and synthesises all
  its audio. That property is worth keeping.
