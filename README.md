# HOODMATCH

**A fast-movement arena FPS written from scratch in C# — bunny-hopping, dual grappling hooks and flat-damage guns, in the spirit of Redmatch 2.**

Free-for-all deathmatch against bots across five arenas. You spawn with full health and a
three-gun loadout plus a knife. Kills pay nothing but **kill points**, which you spend at wall
cabinets to permanently upgrade your health, damage, speed and jump for the match. No game engine:
every sound is synthesised at startup, the weapon viewmodels are animated rigs, and the supplied
map loads directly from a local GLB.

![status](https://img.shields.io/badge/status-early%20access%200.2.3-ffba38)
![language](https://img.shields.io/badge/C%23-.NET%2010-512bd4)
![framework](https://img.shields.io/badge/raylib--cs-8.1.0-2f6f4e)
![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)
![license](https://img.shields.io/badge/license-MIT-blue)

> ### Early access
> `0.2.3` is a playable single-player-vs-bots worktree: one game mode, five maps, no networking.
> The simulation is deterministic given a seed, and every build is verified by a headless harness
> before it ships. The latest pass added animated viewmodel rigs, rebuilt firearm audio and a
> camera-anchored sky. See [docs/ROADMAP.md](docs/ROADMAP.md) for what is planned next.

> ### Picking up development?
> Read **[HANDOFF.md](HANDOFF.md)** first — current state, the exact build/verify/release
> workflows, and the open tasks, written to bring a new session up to speed fast.

---

## Quick start

**Download (Windows x64):** grab the self-contained `HoodGlobalOffensive.exe` from the
[latest release](https://github.com/arjunsingh53/hood-global-offensive/releases/latest) — no .NET
install needed, and the map/character/weapon assets are embedded in the single file. Optionally
verify the binary before playing:

```bash
HoodGlobalOffensive.exe --headless --seconds 300 --seed 20260114   # must end in RESULT: PASS
```

**From source:** the [.NET SDK](https://dotnet.microsoft.com/download) 8.0 or newer (this builds
against `net10.0`), and a GPU/display to play. The only package dependency is
[`Raylib-cs`](https://github.com/ChrisDill/Raylib-cs), which ships native raylib binaries for
Windows, Linux and macOS, so there is nothing else to install.

```bash
git clone https://github.com/arjunsingh53/hood-global-offensive.git
cd hood-global-offensive
dotnet run
```

On Windows you can also double-click **`play.cmd`**, which finds a per-user SDK install and
launches the game. Arguments pass straight through.

`dotnet run -- --help` lists every option:

```bash
dotnet run                                    # play
dotnet run -- --maps                          # list the map catalog
dotnet run -- --headless --seconds 300        # full match, no window, prints an invariant report
dotnet run -- --screenshot shot.png --map Arena --screen upgrade
dotnet run -- --sfx-dump sfx                   # write every generated sound cue to WAV
```

---

## Screenshots

All captured from the current build at 1920×1080.

![Rifle gameplay on Industrial Zone](shots/31-industrial.png)

| | |
| --- | --- |
| ![Iron sights ADS](shots/32-iron-sights.png) <br> *Calibrated iron sights* | ![SMG reload](shots/33-reload.png) <br> *Animated rig reload* |
| ![Arena](shots/34-arena.png) <br> *Arena — crates, towers, dais* | ![Playground](shots/35-playground.png) <br> *Playground — everything is climbable* |
| ![Upgrade cabinet](shots/36-upgrade.png) <br> *Kill-point upgrade cabinet* | ![Scoreboard](shots/37-scoreboard.png) <br> *Scoreboard — 15 bots* |
| ![Main menu](shots/30-menu.png) <br> *The front end* | |

---

## Why it feels different

### Movement is the game

There is one run speed, and it is fast (10 m/s). The skill expression is entirely in keeping the
momentum you build:

- **Bunny-hop.** Hold `Space` to auto-hop. As long as you hold `Space` your speed is preserved on
  the jump frame — but the moment you press `WASD` while grounded, ground friction eats your
  momentum. Hitting the floor with movement keys held is how you die.
- **Dual grappling hooks.** `Q` fires the left hook, `E` the right. Hooks pull you toward the
  attach point with a force proportional to the distance, climb walls, arrest falls, and can both
  be anchored at once. Slingshot yourself by anchoring both hooks and releasing one at the right
  moment; the movement ceiling is 45 m/s.
- **Guns are movement tools too.** The Double Barrel's alt-fire launches you; the shotgun and
  revolver shove whoever they hit.

### Gunplay is flat and honest

Damage is **uniform** — head, body and legs all take the same number; there are no headshot
multipliers, no armour, no economy. The only range falloff in the game belongs to the Double
Barrel. What separates the guns is fire rate, magazine, spread behaviour and knockback, and the
spread model is deterministic: idle spread, movement spread, air spread and per-shot bloom are
authored numbers you can read in [`src/Combat/Weapons.cs`](src/Combat/Weapons.cs).

### Kill points, not money

Every kill is **1 kill point**. Cabinet upgrades cost 1 point per level, every stat has 3 levels,
and dying costs you one level from your highest-upgraded stat. A 30-kill match is a race to
compound your advantage without feeding it back.

| Stat | Per level |
| --- | --- |
| Health | +25 max HP |
| Damage | +12% |
| Speed | +6% |
| Jump | +7% |

Stand beside a cabinet and press `E` to open the upgrade menu.

### The presentation keeps pace

- **Animated viewmodel rigs.** The rifle, sniper, SMG and pistol are rigged models with their own
  draw, idle, inspect, reload and shoot clips, blended through smooth aim-down-sights transitions.
  Ads alignment was calibrated per weapon from captured frames, so the iron sights land exactly on
  the reticle.
- **Audio synthesised at startup.** Every cue — guns, hitmarker, footsteps, UI — is generated from
  filter-shaped noise; no sample files ship with the game. Gunshots carry three distance profiles
  (near, distant, wall-occluded) that swap with listener routing. `dotnet run -- --sfx-dump sfx`
  writes all of them to WAV if you want to hear them out of context.
- **Camera-anchored sky.** The dusk gradient, sun and drifting cloud banks track the camera with
  true horizon parallax instead of being painted onto the screen.

---

## Controls

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` | Move — kill your momentum if you touch the ground with these held |
| `Space` | Jump; hold to auto-hop |
| Mouse | Aim |
| Left click | Fire |
| Right click | Aim down sights (iron sight / scope); **Double Barrel: point-blank double blast** |
| `Q` / `E` | Left / right grappling hook. `E` beside a cabinet opens the upgrade menu instead |
| `R` | Reload |
| `V` | Quick knife swing (never switches weapon) |
| `F` | Flashlight (presentation only — it lights nothing) |
| `1`–`4` / wheel | Weapon slots: 3 guns + knife |
| `Tab` | Scoreboard |
| `F2` | Crosshair options |
| `Esc` | Pause |

---

## The weapons

Seven guns and a knife. Reserve ammo is infinite; reloads are instant-ish (0.4–2.2 s) and
interruptible by the usual rules. The SMG, Revolver and Double Barrel take 0.4 s to pull out
after a switch — the other guns raise instantly.

| Weapon | Damage | Fire | Mag | Notes |
| --- | --- | --- | --- | --- |
| Pistol | 25 | semi | 12 | Animated rig, calibrated sights, fast ADS |
| Revolver | 75 | semi, 2.5/s | 6 | Two-tap, heavy kick, perfect accuracy, shoves victims (60 u/s) |
| SMG | 20 | auto, 18.16/s | 40 | Animated rig reload, blooms while held |
| Rifle | 30 | auto, 10/s | 30 | Animated rig, calibrated iron sights |
| Shotgun | 17 × 9 | semi | 6 | Falls to 10% (20–70 m); 140 u/s knockback on hit |
| Sniper | 100 | bolt, 0.83/s | 5 | 18° scope, one shot one kill |
| Double Barrel | 90 | semi, as fast as you click | 2 | Falls to 20% (15–30 m). **Alt-fire: 270-damage blast that launches you** |
| Knife | 55 | 2.5/s | — | 2.5 m, always on slot 4 |

---

## The maps

`--maps` lists the catalog; `--map <name>` picks one everywhere (game, headless, screenshots).

| Map | Description |
| --- | --- |
| `Industrial` | The supplied Low Poly Industrial Zone GLB, with a measured collision sidecar |
| `Arena` | Symmetric quad: centre dais, corner towers, stacked crates, tall pillars |
| `Streets` | Two building rows around a plaza with containers, lamps and parked cars |
| `Ruins` | Broken walls, corner towers and a collapsed bridge over rubble |
| `Playground` | Vertical playground: stepped tower, floating platforms, jump boxes and a high perch |

Every map validates its own spawn list at load time — a spawn that lands inside geometry is
relocated to the nearest clear floor spot, so a bad `Add(...)` call degrades nothing.

---

## Bots and match rules

- Free-for-all, first to **30 kills** (configurable), 10-minute cap.
- **1–15 bots** at four skills: Easy, Normal, Hard, Expert — profile constants live in
  `BotProfile.For` in [`src/Entities/Bot.cs`](src/Entities/Bot.cs).
- Bots navigate an A* graph derived from collision geometry, duo at range, strafe, chase kills
  and use the same shot resolution as you do — there is one definition of what a bullet does.

---

## Verification: the headless harness

The simulation never reads input and never draws, so the entire game runs without a window. The
harness plays a full match with a scripted robot and asserts invariants:

```bash
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114
dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331
```

The report ends in `RESULT: PASS` or `RESULT: FAIL` and checks:

- the player never ends up embedded in geometry
- no transform ever becomes non-finite
- grapple ropes never exceed their own range
- every chosen spawn is clear of geometry, and every map has upgrade stations
- recorded kills and recorded deaths agree, and the match actually produces combat

Both commands above run in CI (`.github/workflows/verify.yml`) alongside a `-warnaserror` build.

---

## Documentation

| Document | Contents |
| --- | --- |
| [docs/GUIDE.md](docs/GUIDE.md) | The player guide: movement tech, weapon reference, cabinets, maps, HUD |
| [docs/BUILDING.md](docs/BUILDING.md) | Build, run, verify, package; asset provenance |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the code is organised and why |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Where the project is going |

---

## Project layout

```
Program.cs                   entry point: game, --headless, --screenshot, --maps, --help
play.cmd                     Windows launcher
src/Core/                    constants, input, settings, the app state machine
src/World/                   maps, collision, movement, grappling, nav graph
src/Combat/                  weapon definitions and the one hitscan function
src/Entities/                Actor, Player, Bot
src/Sim/                     the authoritative simulation and the headless harness
src/Render/ src/UI/ src/Audio/   presentation only
assets/maps/                 the supplied Industrial Zone GLB (CC-BY 4.0, see attribution)
assets/weapons/              animated viewmodel rig pack (owner-supplied sources, see viewmodel-rig-SOURCE.md)
shots/                       curated 1920×1080 captures used in this README (current build)
```

## Credits and licence

MIT licensed — see [LICENSE](LICENSE). HOODMATCH is an unofficial fan project inspired by
[Redmatch 2](https://store.steampowered.com/app/1280770/Redmatch_2/) by Etherous Games; it is not
affiliated with or endorsed by them. The Low Poly Industrial Zone map is by novikto (CC-BY 4.0);
keep its [attribution record](assets/maps/LOW-POLY-INDUSTRIAL-ZONE-ATTRIBUTION.md) with any
distributed build. The animated viewmodel weapon assets are project-owner supplied and are not
covered by this repository's licence; see their
[source record](assets/weapons/viewmodel-rig-SOURCE.md) before redistributing.
