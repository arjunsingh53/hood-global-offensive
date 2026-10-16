# HOODMATCH — Player Guide

**Version:** 0.2.0 · **Engine:** C# / .NET 10 / Raylib-cs

This guide describes the behaviour that is actually implemented in the code. Where the game
deliberately differs from Counter-Strike — and it does, in almost every system — it says so
explicitly.

---

## 1. Getting started

### Requirements

- The **.NET SDK**, version 8.0 or newer. This project targets `net10.0`.
- A GPU and a display to play. `--headless` needs neither.

### Running the game

```bash
git clone https://github.com/arjunsingh53/hood-global-offensive.git
cd hood-global-offensive
dotnet run
```

**Windows shortcut:** double-click `play.cmd`. It sets `DOTNET_ROOT` for per-user SDK installs,
runs the game, and keeps the window open on a crash. Arguments pass through.

### Your first match

1. The main menu opens. Leave the defaults (map Industrial, 9 bots, Normal, first to 30).
2. Press `Enter` or click **START DEATHMATCH**.
3. Move with `W A S D`, look with the mouse. You have full health and your three loadout guns.
4. Hold `Space` to hop; the moment you keep `WASD` held while touching the floor, speed dies.
5. Press `Q` or `E` to fire a grappling hook at whatever you are looking at.
6. Get a kill, walk to a cabinet with a glowing light column, press `E`, and buy an upgrade.
7. `Tab` shows the scoreboard; `Esc` pauses (and releases the mouse).

The match ends at **30 kills** or when the **10-minute** clock runs out. Upgrades are per-match.

---

## 2. Controls

| Input | Action |
| --- | --- |
| `W A S D` | Move |
| `Space` | Jump; **hold** to auto-hop |
| Mouse | Aim |
| Left click | Fire |
| Right click | Aim down sights / scope; **Double Barrel: alt blast** |
| `Q` / `E` | Left / right hook. Beside a cabinet, `E` opens the upgrade menu |
| `R` | Reload |
| `V` | Quick knife (swings without switching weapons) |
| `F` | Flashlight toggle (pure presentation, lights nothing) |
| `1`–`4`, wheel | Three gun slots plus the knife |
| `Ctrl` / `C` | Crouch / crouch toggle (slower, tighter spread) |
| `Tab` | Scoreboard |
| `F2` | Crosshair options (also available from the main menu and pause) |
| `Esc` | Pause |

---

## 3. Movement

Movement is the whole game. There is a single run speed — there is no sprint key and no
walk-modifier — and everything is about keeping the speed you build.

### The bunny-hop rule

- **Hold `Space`** and you auto-hop every time you touch the ground. A jump frame never pays
  friction, so your momentum carries.
- **Holding `WASD` while grounded pays double friction.** That is the entire trade: hopping
  preserves speed; steering on the floor deletes it. Good players jump, steer mid-air lightly
  (air acceleration only really bites below ~1.4 m/s of wish speed), and re-plant with `Space`.
- Walking off a step instead of jumping keeps your speed too — no fall damage exists.

### Grappling hooks

- `Q` fires the left hook, `E` the right. Each hook is independent; you can have both anchored.
- Hooks attach to the **first surface within 70 m** and pull with a force that scales with rope
  length (up to 60 m/s of pull), plus a climb assist that lets you ascend walls.
- The hook releases automatically at 2.25 m of rope length, or you can fire that side again to
  cut it. Anchor both, release one, and the asymmetric pull becomes a **slingshot** — the fastest
  legal speed in the game is 45 m/s, and hook momentum is how you reach it.
- Ropes are real: they never stretch past their own limit, and the headless harness asserts it.

### Numbers you can feel

| Constant | Value |
| --- | --- |
| Run speed | 400 u/s (10 m/s) |
| Jump | 330 u/s, gravity 700 u/s² |
| Terminal fall | 1100 u/s; grapple climb 1600 u/s |
| Speed cap | 1800 u/s (45 m/s) |
| Step height | 20 u (0.5 m); floor snap 8 u |
| Hook range | 2800 u (70 m) |
| Crouch | 54 u tall, 140 u/s move; stand 72 u |

---

## 4. Gunplay

### The one-line summary

Damage is flat. Head, body and legs take the same number, at any range (the Double Barrel is the
only gun with falloff). There is no armour and no headshot bonus. A gun is defined by its fire
rate, magazine, spread behaviour and knockback.

### The spread model

Every shot scatters inside a cone built from authored, deterministic terms — never from RNG of
wall-clock state:

- **Idle spread** — the base cone (0.28°–5.5° depending on gun; shotgun idles wide).
- **Movement spread** — scales with your horizontal speed; this is why bhop shots are wild.
- **Air spread** — a flat penalty while airborne; some guns punish it hugely (Sniper +22°).
- **Bloom** — grows per shot up to a cap, decays over time; ADS multiplies the idle/bloom parts
  only, so aiming does not fix a moving shot.

Crouching cuts spread to 62%, but cuts your speed to 140 u/s. Spread persists through reloads.

### Recoil

Each shot applies a deterministic view kick: vertical punch ramps up over the first shots, plus a
small, seeded horizontal wobble that grows over the first five shots. The shot itself always lands
on the crosshair — the kick is applied after it. Let the recovery delay (~0.2–0.38 s) lapse and
the punch resets.

### The weapons

| Weapon | Dmg | Fire | Mag | Reload | Notes |
| --- | --- | --- | --- | --- | --- |
| Pistol | 25 | semi, 8.3/s | 12 | 1.2 s | Fast ADS, 50% ADS spread |
| Revolver | 75 | semi, 2.5/s | 6 | 2.0 s | 100% accuracy at all times, 60 u/s knockback; it is a hand cannon |
| SMG | 20 | **auto**, 18.16/s | 40 | 1.0 s | Melts up close, blooms fast |
| Rifle | 30 | **auto**, 10/s | 30 | 1.5 s | Calibrated iron sights; the all-rounder |
| Shotgun | 17 × 9 | semi | 6 | 2.0 s | Falls from 100% to 10% between 20 m and 70 m |
| Sniper | 100 | bolt, 0.83/s | 5 | 2.2 s | 18° scope, 0.25× ADS spread, one shot = one kill |
| Double Barrel | 90 | semi, click-fast | 2 | 1.33 s | Falls to 20% past 15 m, reaching the floor at 30 m |
| Knife | 55 | 2.5/s | — | — | 2.5 m reach, always on slot 4, `V` quick-swings |

All reserve ammo is infinite — you only manage magazine, reload timing and trigger discipline.

### The Double Barrel's alt-fire

Right-click does not aim: it spends a shell as a **270-damage point-blank blast** with 8 pellets
of extra spread, 380 u/s of knockback on victims (the gun's 9.5 m/s secondary stat) — and **380 u/s
of knockback on you**, upward too. Aiming it at the floor is a jump. The Shotgun and Double Barrel
are the only guns whose damage changes with distance.
This is also why your right-click is not "aim" on this weapon and nothing you read in other guides
applies.

---

## 5. Kill points and cabinet upgrades

- Every kill: **1 kill point**.
- Every upgrade level costs **1 point**.
- Four stats, **3 levels each**:
  - **Health**: +25 max HP per level
  - **Damage**: +12% per level
  - **Speed**: +6% per level
  - **Jump**: +7% per level
- Stand beside a **cabinet** (the box with a glowing amber light column) and press `E`.
- **Dying drops one level from your highest-upgraded stat.** A runaway lead is involuntarily shared.

The HUD shows your upgrade pips (HP / DMG / SPD / JMP) beside the health bar the whole match.

---

## 6. Reading the HUD

- **Health card** — big current HP; the pips under it are your upgrade levels.
- **Kill points** — your current spendable points.
- **Weapon strip** — four slots with the active one highlighted; reserve is ∞ so only magazine
  rounds are shown.
- **Hook squares** — `Q` and `E` indicators that light up while each hook is anchored.
- **Cabinet prompt** — `[ E ] UPGRADE CABINET` appears when you are in range.
- **Radar** — brushes drawn in their surface colours with every cabinet marked, clipped to a circle.
- **Kill feed**, hitmarkers and damage arcs as you would expect.

---

## 7. The maps

| Map | Description |
| --- | --- |
| `Industrial` | The supplied Low Poly Industrial Zone GLB with a measured collision sidecar |
| `Arena` | Symmetric quad — centre dais, corner towers, crates, pillars |
| `Streets` | Two building rows around a plaza with containers, lamps and cars |
| `Ruins` | Broken walls, towers and a collapsed bridge over rubble |
| `Playground` | Stepped tower, floating platforms, jump boxes, a high perch |

`dotnet run -- --maps` lists them; `--map <name>` selects one for the game, `--headless` and
`--screenshot`.

---

## 8. Bots

Four skills — **Easy / Normal / Hard / Expert** — with profiles in `BotProfile.For`. Bots share
your exact movement solver, damage rules and hitboxes; higher tiers react faster, aim tighter,
lead their shots better and make better use of the map. 1–15 bots join any match.

---

## 9. Settings

`F2` opens crosshair options anywhere. `Esc` pauses with sensitivity, crosshair and camera
feedback. Preferences persist in `%LOCALAPPDATA%\HoodGlobalOffensive\settings.json` — including
your map and loadout picks (schema v2; older files are upgraded in place, corrupt ones are
archived beside it).

---

## 10. Command line reference

```bash
dotnet run                                     # play
dotnet run -- --maps                           # list maps
dotnet run -- --headless [options]             # full match, no window, invariant report
dotnet run -- --screenshot <file> [options]    # render one frame to a PNG
```

| Option | Applies to | Meaning |
| --- | --- | --- |
| `--seconds <n>` | headless / screenshot | Simulated seconds (default 90 / 4) |
| `--bots <n>` | all | Bot count (default 9, max 15) |
| `--skill <level>` | all | `Easy` `Normal` `Hard` `Expert` |
| `--seed <n>` | all | Deterministic match seed |
| `--map <name>` | all | Map from the catalog (default `Industrial`) |
| `--screen <name>` | screenshot | `play` `scoreboard` `upgrade` `menu` `crosshair` `trauma` `impact` |
| `--weapon <name>` | screenshot | Equip a weapon by catalog name |
| `--zoom` | screenshot | Hold ADS/scope before capturing |
| `--width/--height` | screenshot | Output size (default 1280×720) |
| `--verbose` | headless | Verbose per-tick diagnostics |

Headless runs exit non-zero when an invariant fails, which is what CI gates on:

```bash
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114
dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331
```

---

## 11. Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `You must install or update .NET` when running `bin/**/HoodGlobalOffensive.exe` directly | Per-user SDK installs are not on `PATH`/`DOTNET_ROOT`. Use `dotnet run` or `play.cmd`. |
| `--screenshot` produces nothing | It needs a real GPU/window; headless servers cannot capture. |
| Shots feel random while moving | That is the movement spread term, not RNG — stand, crouch or tap to tighten. |
| "My hook does not fire" | `E` near a cabinet is interact, not a hook. Step away or use `Q`. |
| Sound is missing | All audio is synthesised at startup from `WavGen`; a failed device logs to stderr. |

Because the simulation is deterministic, the fastest bug report is
`dotnet run -- --headless --seconds 300 --seed <n>` with what you expected.
