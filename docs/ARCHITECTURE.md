# Architecture

Hood Global Offensive is a single C# project with no engine underneath it. It exists as roughly
6,000 lines across 24 source files, and the organising principle is a hard split between
**a deterministic simulation** and **a presentation layer**.

That split is not architectural purity for its own sake: it is what makes the game testable without
a window and without a GPU, which in turn is how the bugs documented in the README were found.

---

## The one big rule

> **The simulation never reads input and never draws. The presentation never changes game state.**

- Everything that decides what happens — physics, shooting, damage, scoring, bot decisions — lives
  under `src/Sim`, `src/World`, `src/Combat` and `src/Entities`, and takes the form
  `Advance(input, dt)`.
- Everything that turns that state into pixels or sound lives under `src/Render`, `src/UI` and
  `src/Audio`, and only reads.

The reason this matters in practice: the same `Simulation.Update(input, dt)` call that drives the
real game is also driven by a **scripted input source** in the headless harness, so a full match can
be played with no window at all.

```
                ┌───────────────────────────────────────────┐
                │            Simulation                      │
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
│ ScriptedInput  │  ← HeadlessRunner's robot player          │ Hud / Screens (2D)│
│                │                                           │ AudioBank         │
└────────────────┘                                           └───────────────────┘
```

---

## Layers

### `src/Core` — foundation

| File | Responsibility |
| --- | --- |
| `Cfg.cs` | **Every gameplay constant in one table.** Sizes, speeds, gravity, step height, damage rules, match rules, view settings, bot counts. If you want to change how the game feels, start here and nowhere else. |
| `Util.cs` | Vector/math helpers, colour helpers, and `Rng` — a deterministic xorshift generator so that a given seed always produces the same match. Determinism is what lets a failing headless run be reproduced. |
| `Input.cs` | `InputFrame` (the abstract set of things a player can want to do this tick), `RealInput` (reads raylib), `ScriptedInput` (the robot player). |
| `Game.cs` | Window creation, the state machine (menu → playing → paused → buy → match over), and the frame loop. Also owns the view-model render target. |

### `src/World` — space and movement

| File | Responsibility |
| --- | --- |
| `Geometry.cs` | `Aabb`, the only primitive in the game, with ray/box intersection. The entire world is axis-aligned boxes, which is why the collision code is short enough to reason about. |
| `GameMap.cs` | The "Crossfire" arena expressed as data: a list of `Add(x0, x1, y0, y1, z0, z1, kind, penetrable)` calls. Also owns the world queries the rest of the game uses — ray hits, line of sight, box overlap, floor finding, and spawn selection. |
| `CharacterMover.cs` | The Quake-derived character controller: accelerating, air-accelerating with a per-tick cap, friction, gravity, and swept collision with step-up. |
| `NavGraph.cs` | Samples the map geometry onto a grid to build a waypoint graph (895 nodes on Crossfire), then runs A* over it for the bots. It rebuilds itself from the brushes at startup, so editing the map automatically updates bot pathing. |

### `src/Combat` — the damage pipeline

| File | Responsibility |
| --- | --- |
| `Weapons.cs` | `WeaponDef` — the immutable description of a gun (damage, falloff, magazine, spread parameters, penetration, and the 30-entry spray `Pattern`) — plus `Weapon`, the runtime state (magazine, bloom, shot index, reload timing) and the rule that turns a shot index into a recoil offset. |
| `Hitscan.cs` | One function that turns a ray into a `ShotResult`: world hit, actor hit, hit group, penetration, and final damage. Shared by the player *and* the bots, so there is exactly one definition of "what a bullet does". |

### `src/Entities` — things that exist

| File | Responsibility |
| --- | --- |
| `Actor.cs` | Everything the player and the bots have in common: position, velocity, the three hitboxes, health, armour, helmet, money, inventory, and `ApplyDamage` — the single place where the leg/stomach multipliers and the armour split happen. |
| `Player.cs` | View angles, view kick and punch, zoom state, weapon selection and switch timing, and all the view-model animation state (bob, sway, recoil kick, reload dip, landing dip). |
| `Bot.cs` | Perception (view cone, hearing, line of sight), the state machine (patrol → hunt → engage → dead), A* navigation, duelling behaviour (strafe, hold range, take cover), burst discipline, its own recoil compensation, and `BotProfile` — the four difficulty tiers. |

### `src/Sim` — the authoritative world

| File | Responsibility |
| --- | --- |
| `Simulation.cs` | The whole game as one object: the actor list, the tick loop, weapon firing, hit resolution and damage application, scoring and kill attribution, respawning and spawn selection, and match-over detection. |
| `Effects.cs` | Purely cosmetic-but-simulated state: tracers, impact particles, decals, floating damage numbers, the kill feed, damage direction arcs, and pending sound events. |
| `HeadlessRunner.cs` | Runs a `Simulation` with `ScriptedInput` for N simulated seconds and prints a report, including the invariant checks. |

### `src/Render`, `src/UI`, `src/Audio` — presentation

| File | Responsibility |
| --- | --- |
| `Renderer.cs` | Sky, world brushes, characters, effects, and the transformed first-person view model. |
| `ScreenshotCapture.cs` | Runs a match for a few seconds, waits for a frame worth capturing, and renders exactly one frame to a PNG. |
| `Ui.cs` / `Hud.cs` / `Screens.cs` | Immediate-mode drawing primitives; the gameplay HUD; and the main menu, pause menu, buy menu, scoreboard and match-over screen. |
| `WavGen.cs` / `AudioBank.cs` | Procedural WAV synthesis (gunshots, hits, footsteps, the buy blip, stingers) and a bank that plays them with distance attenuation. |

---

## Design decisions worth knowing

### Why the whole world is axis-aligned boxes

Every brush is an AABB. That makes ray/box and box/box intersection trivial, makes swept collision
cheap, and — most importantly — lets the navigation graph be *derived* from the geometry rather than
authored by hand. Adding a crate is one line, and bot pathing updates itself.

The trade is that diagonal geometry is impossible. The arena is built from steps, ledges and
pillars to compensate, which is part of why it reads as a Counter-Strike map rather than a modern
one.

### Why the recoil is applied after the shot

`Weapon.NextRecoil()` returns an offset for the *next* shot, and firing applies damage using the
pre-kick aim. This reproduces Counter-Strike's most important gunplay property: the first bullet is
always exactly where you aimed, and everything after it is a pattern you have to fight.

### Why the view model cannot be occluded

The first-person weapon is drawn **in its own render pass into a separate render target**, with the
camera pinned to the origin and oriented the same way as the main camera. The render target is then
blitted over the world. Because the weapon never shares a depth buffer with the level, a wall can
never clip it — the failure you get from naively drawing a weapon at world scale.

This was not the first approach. `Rlgl.DisableDepthTest()` was tried first and does **not** work
through raylib's batched renderer, which is why the render-target approach exists. It is also why
`Game.cs` owns the target's lifetime and why `Renderer.Shutdown()` resets it.

### Why audio is synthesised

There are no binary assets in the repository at all. Every sound is generated to a WAV buffer at
startup by `WavGen`, which keeps the repository text-only, makes the sounds tunable in code, and
removes any licensing question about shipped samples.

### Why the far clip plane is set explicitly

raylib's default far clip plane is 1000 units, which is far too small for a 2400-unit arena. The
renderer calls `Rlgl.SetClipPlanes(1.0, 9000.0)` before each `BeginMode3D`. If you add distant
geometry and it disappears, this is why.

### Why `ScriptedInput` exists

Because "does the game still work" is otherwise unanswerable without playing it. `ScriptedInput`
drives a robot player that moves around the map, acquires targets and shoots, and `HeadlessRunner`
then asserts invariants on the result:

- the player never ends up embedded in geometry
- no transform ever becomes non-finite
- recorded kills and recorded deaths agree
- every chosen spawn is clear of geometry
- the match actually produces shots and kills

That last check is what turns a "the game ran without crashing" run into a "the game did something"
run.

---

## Adding a feature

The pattern that keeps the design intact:

1. **A tunable number?** Put it in `Cfg.cs`, or in the relevant definition table (`WeaponDef`,
   `BotProfile`). Never inline it.
2. **A new game rule?** It belongs in `Simulation.cs` (or `Actor.cs` if it is per-entity state), and
   it must be reachable from `Advance(input, dt)` so the headless harness exercises it
   automatically.
3. **A new visual or sound?** Read the state in `Renderer`/`Hud`/`Effects`. Do not write to the
   simulation from there.
4. **A new map element?** One `Add(...)` call in `GameMap.cs`. If it is a floor, the nav graph picks
   it up on its own.
5. **Then run `dotnet run -- --headless --seconds 300`** before you believe it works.
