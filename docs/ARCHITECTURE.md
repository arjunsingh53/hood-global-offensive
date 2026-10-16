# Architecture

HOODMATCH is a single C# project with no engine underneath it. The organising principle is a hard
split between **a deterministic simulation** and **a presentation layer**.

That split is not architectural purity for its own sake: it is what makes the game testable without
a window and without a GPU, which is how the bugs documented in the changelog were found.

---

## The one big rule

> **The simulation never reads input and never draws. The presentation never changes game state.**

- Everything that decides what happens — physics, grappling, shooting, damage, upgrades, bot
  decisions — lives under `src/Sim`, `src/World`, `src/Combat` and `src/Entities`, and takes the
  form `Advance(input, dt)`.
- Everything that turns that state into pixels or sound lives under `src/Render`, `src/UI` and
  `src/Audio`, and only reads.

The same `Simulation.Update(input, dt)` call that drives the real game is also driven by a
**scripted input source** in the headless harness, so a full match can be played with no window.

```
                ┌───────────────────────────────────────────┐
                │              Simulation                    │
                │  (pure state + logic, no drawing)          │
                └───────────────┬───────────────┬────────────┘
                                │               │
             InputFrame         │               │  reads state
        ┌───────────────────────┘               └──────────────────────┐
        │                                                              │
┌───────┴────────┐                                           ┌─────────┴─────────┐
│ Input sources  │                                           │   Presentation    │
│                │                                           │                   │
│ RealInput      │  ← keyboard + mouse                       │ Renderer (3D)     │
│ ScriptedPlay   │  ← HeadlessRunner's robot player          │ Hud / Screens (2D)│
│                │                                           │ AudioBank         │
└────────────────┘                                           └───────────────────┘
```

---

## Layers

### `src/Core` — foundation

| File | Responsibility |
| --- | --- |
| `Cfg.cs` | **Every gameplay constant in one table.** Speeds, gravity, hook physics, step height, damage rules, upgrade magnitudes, match rules. If you want to change how the game feels, start here and nowhere else. |
| `Util.cs` | `U` — vector/math helpers — and `Rng`, a deterministic xorshift generator so a given seed always produces the same match. Determinism is what lets a failing headless run be reproduced. |
| `Input.cs` | `InputFrame` (the abstract set of things a player can want this tick) and `RealInput` (reads raylib). |
| `Game.cs` | Window creation, the state machine (menu → playing → paused → upgrade → match over), and the frame loop. Also owns the view-model render target. |
| `UserSettingsStore.cs` | Versioned, bounded local preference persistence (schema v2: menu choices, crosshair, camera feedback, map, loadout). Recovers from corrupt files and says so on stderr. |

### `src/World` — space, movement and grappling

| File | Responsibility |
| --- | --- |
| `Geometry.cs` | `Aabb`, the only collision primitive in the game, with ray/box intersection. |
| `MapRegistry.cs` | The map catalog: names, the four box-arena layouts, the Industrial GLB delegation, spawn lists and colour palettes. |
| `GameMap.cs` | One arena: collision brushes, validated spawns, and every world query the game uses — ray hits, line of sight, box overlap, floor finding, spawn selection. Also relocates any authored spawn that lands inside geometry. |
| `CharacterMover.cs` | The Quake-derived character controller: ground/air acceleration with the bhop friction rule, gravity, swept collision with step-up, ground snap, and an unstick rescue. |
| `Grapple.cs` | The two-hook rope model: attach raycast, per-tick pull, climb assist, auto-release, rope length limits. |
| `NavGraph.cs` | Samples collision geometry onto a grid to build a waypoint graph and runs A* over it for the bots. Rebuilds itself from brushes at startup, so new maps get pathing for free. |

### `src/Combat` — the damage pipeline

| File | Responsibility |
| --- | --- |
| `Weapons.cs` | `WeaponDef` — the immutable gun description (flat damage, optional range tiers, spread terms, bloom, recoil, ADS, secondary fire, knockback) — plus `Weapon`, the runtime state, its deterministic `SpreadFor(...)` and per-shot kick. |
| `Hitscan.cs` | One function that turns a ray into a `ShotResult`: world hit, actor hit, penetration, and final damage. Shared by the player *and* the bots, so there is exactly one definition of "what a bullet does". |

### `src/Entities` — things that exist

| File | Responsibility |
| --- | --- |
| `Actor.cs` | Everything the player and bots share: position, velocity, hitboxes, health, kill points, upgrade levels (`UpgradeHealth/Damage/Speed/Jump`), and `ApplyDamage` — the single place where damage lands. |
| `Player.cs` | View angles, view kick, zoom/ADS state, weapon selection including the quick knife, hook ownership, cabinet interaction intent, and view-model animation state. |
| `Bot.cs` | Perception, the state machine (patrol → hunt → engage → dead), A* navigation, duelling behaviour, burst discipline, recoil compensation, and `BotProfile` — the four difficulty tiers. |

### `src/Sim` — the authoritative world

| File | Responsibility |
| --- | --- |
| `Simulation.cs` | The whole game as one object: actor list, tick loop, firing, hit resolution and damage, kill points and the death level-penalty, upgrade purchases, rope updates, respawning, spawn selection, and match-over detection. |
| `Effects.cs` | Simulated-but-cosmetic state: tracers, impact particles, decals, damage numbers, the kill feed, damage arcs, pending sound events. |
| `HeadlessRunner.cs` | Runs a `Simulation` with the `ScriptedPlay` robot for N simulated seconds and prints a report, including the invariant checks. |

### `src/Render`, `src/UI`, `src/Audio` — presentation

| File | Responsibility |
| --- | --- |
| `Renderer.cs` | Sky, the map (GLB scene for Industrial; kind-coloured brushes for the box arenas), cabinets with their beacon columns, grapple ropes, the flashlight cone, characters, effects, and the transformed first-person view model. |
| `ScreenshotCapture.cs` | Runs a match for a few seconds and renders one frame to a PNG (seven staged screens). |
| `Ui.cs` / `Hud.cs` / `Screens.cs` | Immediate-mode primitives; the HUD (health, build pips, kill points, hook indicators, radar, cabinet prompt); main menu, pause, upgrade cabinet, crosshair options, scoreboard and match-over. |
| `WavGen.cs` / `AudioBank.cs` | Procedural WAV synthesis (gunshots, hits, footsteps, hooks, upgrades, blips) and a bank that plays them with distance attenuation. |

---

## Design decisions worth knowing

### Why the whole world is axis-aligned boxes

Every brush is an AABB. That makes ray/box and box/box intersection trivial, makes swept collision
cheap, and — most importantly — lets the navigation graph be *derived* from the geometry rather
than authored by hand. Adding a crate is one line, and bot pathing updates itself.

The four box arenas are **rendered from the same brushes they collide with** — there is no
duplicate visual geometry to drift out of sync. The supplied Industrial GLB is the one exception:
its visible meshes come from the asset and its collision comes from a measured sidecar in
`IndustrialZoneLayout.cs`. `GameMap.RendersGlb` switches the renderer between the two.

### Why the shot lands on the crosshair and the kick comes after

Firing resolves damage using the pre-kick aim and applies the view punch afterwards. The first
bullet is always exactly where you aimed, and the pattern after it is a rhythm you fight — while
keeping the damage model flat (no headshot multiplier, no armour) as the reference does.

### Why the view model cannot be occluded

The first-person weapon is drawn **in its own render pass into a separate render target**, with the
camera pinned to the origin and oriented the same way as the main camera, then blitted over the
world. Because the weapon never shares a depth buffer with the level, a wall can never clip it.

### Why audio is synthesised

Every sound is generated to a WAV buffer at startup by `WavGen`, which keeps the repository almost
entirely text, makes the sounds tunable in code, and removes any licensing question about shipped
samples. The binary assets are the Industrial GLB (supplied, with attribution), and the generated
character/weapon meshes with checked-in build scripts.

### Why `ScriptedPlay` exists

Because "does the game still work" is otherwise unanswerable without playing it. The robot moves,
hops, fires hooks, buys upgrades and shoots; `HeadlessRunner` asserts invariants on the result:

- the player never ends up embedded in geometry
- no transform ever becomes non-finite
- grapple ropes never exceed their own range
- every spawn is clear of geometry and every map has cabinets
- recorded kills and deaths agree, and the match actually produces combat

That last pair is what turns a "ran without crashing" run into a "did something" run. CI runs the
harness twice with fixed seeds plus a `-warnaserror` build.

### Why spawns are sanitized

New maps are authored as lists of `Add(...)` brushes and `S(x, z, yaw)` spawns. Because a hand-typed
spawn can land inside a brush, `GameMap` validates every spawn against the collision it just built
and relocates offenders to the nearest clear floor spot — deterministically, nearest-ring-first.
Authors cannot ship a spawn that traps the player, and the harness asserts it.

---

## Adding a feature

The pattern that keeps the design intact:

1. **A tunable number?** Put it in `Cfg.cs`, or the relevant table (`WeaponDef`, `BotProfile`).
   Never inline it.
2. **A new game rule?** It belongs in `Simulation.cs` (or `Actor.cs` if per-entity), reachable from
   `Advance(input, dt)` so the headless harness exercises it automatically.
3. **A new visual or sound?** Read the state in `Renderer`/`Hud`/`Effects`. Never write to the
   simulation from there.
4. **A new map?** Add a name to `MapRegistry.Names`, a branch in `AddCollision`/`CreateSpawns`, and
   your `Add(...)`/`S(...)` calls. Bot pathing, bot count, cabinets and spawn validation come free.
5. **Then run `dotnet run -- --headless --seconds 300`** before you believe it works.
