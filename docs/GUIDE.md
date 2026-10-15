# Hood Global Offensive — Player Guide

**Version:** early access v0.1.0 (map: Crossfire) · **Engine:** C# / .NET 10 / Raylib-cs

This is the complete guide to playing Hood Global Offensive. Everything below describes the
behaviour that is actually implemented in the code — where the implementation differs from what
you might expect from Counter-Strike, it says so explicitly rather than quietly glossing over it.

---

## Contents

1. [Getting started](#1-getting-started)
2. [The main menu](#2-the-main-menu)
3. [Controls](#3-controls)
4. [Reading the HUD](#4-reading-the-hud)
5. [Movement](#5-movement)
6. [How shots work](#6-how-shots-work)
7. [Damage, hitboxes and armour](#7-damage-hitboxes-and-armour)
8. [Spread and accuracy](#8-spread-and-accuracy)
9. [Recoil and spray control](#9-recoil-and-spray-control)
10. [Weapon reference](#10-weapon-reference)
11. [Economy and buying](#11-economy-and-buying)
12. [Match rules](#12-match-rules)
13. [Bots](#13-bots)
14. [The map: Crossfire](#14-the-map-crossfire)
15. [Tactics and practice drills](#15-tactics-and-practice-drills)
16. [Command line reference](#16-command-line-reference)
17. [Troubleshooting](#17-troubleshooting)
18. [Under the hood: known quirks](#18-under-the-hood-known-quirks)
19. [FAQ](#19-faq)

---

## 1. Getting started

### Requirements

- The **.NET SDK**, version 8.0 or newer. This project targets `net10.0`.
- A GPU and a display. Nothing else — the rendering library ships its own native binaries for
  Windows, Linux and macOS.
- Roughly 100 MB of disk for the SDK build output.

### Running the game

```bash
git clone https://github.com/arjunsingh53/hood-global-offensive.git
cd hood-global-offensive
dotnet run
```

**Windows shortcut:** double-click `play.cmd` in the project folder. It sets `DOTNET_ROOT`,
changes to the right directory, runs the game, and keeps the console window open if the game
crashes so you can read the error. Any arguments you give it are passed through to the game.

The very first run compiles the project and takes a few seconds longer than subsequent runs.

### Your first match

1. The main menu opens. Leave the defaults for now (9 bots, Normal skill, 30 kills, sensitivity 2.2).
2. Press `Enter` or click **START DEATHMATCH**.
3. The mouse cursor is captured. Look around with the mouse, move with `W A S D`.
4. You spawn with a **knife** and a **Glock 17**. Left click to shoot.
5. Press `B` to open the buy menu, and buy an **AK-47** ($2700) or **M4A4** ($3100) as soon as you
   can afford one. Kills pay for it.
6. Press `Tab` to see the scoreboard.
7. Press `Esc` to pause at any time — this also releases the mouse cursor.

The match ends when you reach **30 kills**, or when the **10-minute** clock runs out. You spawn
with full health, a knife and a pistol, and you keep every weapon you have bought through
respawns.

---

## 2. The main menu

| Setting | Default | What it does |
| --- | --- | --- |
| **START DEATHMATCH** | — | Begins a fresh match with the settings below |
| **BOTS** | 9 | Opponents in the match. The maximum is 15 |
| **BOT SKILL** | Normal | Easy / Normal / Hard / Expert — see [Bots](#13-bots) |
| **KILL LIMIT** | 30 | Kills needed to win the match |
| **MOUSE SENSITIVITY** | 2.2 | Degrees of rotation per mouse count = `sensitivity × 0.04` |
| **QUIT** | — | Exit the game |

Navigate with the mouse or the arrow keys; change values the same way or with `Enter`.

**There is no persistence.** Settings are not written to disk, so a fresh launch resets them to the
defaults. Note the values down if you find a setup you like, or change the defaults in
[`src/Core/Cfg.cs`](../src/Core/Cfg.cs).

---

## 3. Controls

| Input | Action |
| --- | --- |
| `W` `A` `S` `D` | Move forward / left / back / right |
| Mouse | Aim |
| Left click | Fire |
| Right click | Toggle scope (**AWP only**) |
| `R` | Reload |
| `1` | Knife |
| `2` | Pistol (secondary) |
| `3` | Primary weapon |
| `Shift` | Walk — quiet and slower, for peeking without giving away your position |
| `Ctrl` | Crouch — smaller hitbox, tighter spread, slower |
| `Space` | Jump |
| `B` | Open the buy menu (only while alive) |
| `Tab` (hold) | Scoreboard |
| `Esc` | Pause menu |

Notes:

- **Keys are not rebindable** in v0.1.0.
- Opening the buy menu or pausing releases the mouse cursor, so you may leave the window. Closing
  them recaptures it.
- Weapon switching while reloading cancels the reload.

---

## 4. Reading the HUD

| Element | Where | Notes |
| --- | --- | --- |
| **Crosshair** | Centre | The gap is driven by your live spread value, so it physically widens as you move and fire, and tightens as you stand still or crouch |
| **Health** | Bottom left | Turns to a low-health pulse when you are badly hurt |
| **Armour + helmet** | Bottom left | The bar and label tell you whether you currently have kevlar |
| **Ammo** | Bottom right | `magazine / reserve` |
| **Money** | Bottom left area | Current cash for the buy menu |
| **Kill feed** | Top right | Colour-coded, with a marker for headshots |
| **Hitmarker** | Centre | Brief confirmation that your shot connected |
| **Damage numbers** | In world | Floating numbers at the point of impact |
| **Damage direction arcs** | Around the centre | Show which direction incoming fire came from |
| **Radar** | Top left | Only reveals enemies you currently have line of sight to — see below |
| **Scoreboard** | Centre, while `Tab` is held | Kills, deaths, accuracy and headshots for you and every bot |
| **Death overlay** | Centre | Shows you died and counts down the respawn |

### The radar is honest

The radar does **not** show you where enemies are. It shows you where enemies are **when you can
actually see them**. Every blip is confirmed by a line-of-sight raycast at that instant. Enemies
behind cover, around corners or on the other side of the map are simply absent.

This means you cannot use the radar to pre-aim at someone you have not seen. It is a confirmation
tool, not a wallhack — and it cuts both ways, because the bots perceive you under the same rules.

---

## 5. Movement

The character controller is a port of the Quake/CS model, which is why the movement has a
particular feel: momentum matters, and you can accelerate in the air in ways that a modern engine
would not allow.

### Base speeds (world units per second)

| State | Speed | Notes |
| --- | --- | --- |
| Run (forward) | **250** | The reference speed; `peak horizontal speed` in the test report hovers around 251 |
| Walk (`Shift`) | **130** | Quiet, and does not widen your spread as much |
| Crouch (`Ctrl`) | **85** | Also halves your height and tightens spread by 38% |
| Jump | **300** upward | |
| Gravity | **800** /s² | |

A standing player is **72 units** tall, crouched **54 units**. Your eye is at 64 units standing,
46 crouched. For scale: a doorway in the west corridor is 600 units wide and the arena is
2400 × 2400 units.

### Jumping and climbing

Jump velocity of 300 against gravity of 800 gives a jump apex of exactly **56.25 units** above your
feet. That number matters, because it is what makes the map's vertical layout work:

- **Step-up:** while walking you automatically step onto ledges up to **20 units** high. You do not
  need to jump for kerbs and low crates.
- **Ledges up to ~56 units** can be reached with a jump.
- The **east terrace is 64 units high** — you cannot jump straight onto it from the floor. Use the
  stepped blocks on its west edge (see [the map](#14-the-map-crossfire)).

### Air control and strafe-jumping

While airborne, your acceleration is capped: any single tick of air movement contributes at most
**30 units/s** of horizontal speed. This is the mechanic that makes strafe-jumping work in
Counter-Strike, and it is deliberately preserved here.

The practical technique:

1. Run forward and jump.
2. While in the air, hold `A` (or `D`) and turn your mouse smoothly in that same direction.
3. Release the strafe key and press the opposite one as you swing the mouse back the other way.

Each swing converts a little of your turning into forward speed, and because the cap applies per
tick rather than as a total, correct rhythm adds up. Turning the mouse *into* the strafe is what
matters; pressing `A` while looking straight ahead does nothing useful.

You do not have to do this to play well. It matters mostly for crossing open ground under fire
and for hopping between cover pieces quickly.

### Crouch

Crouching does three things: it drops your eye height, shrinks your hitbox, and multiplies your
spread by **0.62**. It is the correct response to a long-range duel and to holding an angle.

The game refuses to let you stand up if there is a ceiling directly above you, so you will stay
crouched when you release `Ctrl` under the metal roof block at mid — that is intentional, not a
stuck key.

---

## 6. How shots work

Every weapon in the game is **hitscan**. There are no travelling bullets: when you fire, the game
traces a ray from your eye along your aim direction and resolves the first thing it hits, in the
same tick.

For each shot the game resolves, in order:

1. **Your aim direction**, after your view kick (recoil) and a random spread cone are applied.
2. **The nearest world brush** along that ray.
3. **The nearest enemy hitbox** along that ray.
4. Whichever is closer wins. If an enemy is behind a wall, the wall wins.

Each bullet is drawn as a tracer so you can see where it went, and impacts leave a decal and a
small particle burst.

### Shooting through cover

Some brushes are marked **penetrable** — currently the thin cover wall inside the west corridor.
Shooting a penetrable brush does not stop the bullet. Instead:

- The ray continues past it to whatever is behind.
- Damage that reaches a victim through cover is multiplied by that weapon's **penetration damage
  scale** (0.55 by default).
- The Desert Eagle, AK-47, M4A4 and AWP have a **penetration power** of 1 or 2, and the AWP gets a
  much better penetration scale of 0.85.

Hard geometry (walls, crates, the terrace) is not penetrable at all and always stops the bullet.

---

## 7. Damage, hitboxes and armour

This section is the exact damage model. It is short, and understanding it changes how you play.

### Step 1 — base damage

Each weapon has a base damage figure (see the [weapon table](#10-weapon-reference)).

### Step 2 — distance falloff

If the weapon has falloff, damage is reduced exponentially with distance:

```
damage = base × (1 − falloff)^(distance / 1000)
```

Distances are in world units, and the exponent is measured per **1000 units**. Worked examples:

| Weapon | Falloff | At 1000 units | At 2000 units | Meaning |
| --- | --- | --- | --- | --- |
| AK-47 | 0.25 | 27 | 20 | Barely cares about range |
| Desert Eagle | 0.25 | 47 | 35 | Still hurts a long way out |
| Glock 17 | 0.35 | 18 | 12 | Falls off noticeably |
| MP5-SD | 0.32 | 18 | 12 | Get closer |
| Nova | 0.72 | 7 per pellet | 2 per pellet | Only viable in someone's face |

### Step 3 — hitbox multiplier

Each actor has three hitboxes: **head**, **body** (chest and stomach together) and **legs**.

| Hit | Multiplier |
| --- | --- |
| Head | The weapon's headshot multiplier — **×4** for most guns, ×3 for the knife and Nova, ×1.5 for the AWP |
| Body | ×1.0 |
| Legs | **×0.75** |

There is **no separate stomach hitbox** in v0.1.0, even though the code contains a 1.25× stomach
multiplier. See [known quirks](#18-under-the-hood-known-quirks).

To make the headshot numbers concrete, against an unarmoured target at point blank:

| Weapon | Body | Head |
| --- | --- | --- |
| AK-47 (36) | 36 | **144 — instant kill** |
| M4A4 (33) | 33 | 132 — instant kill |
| Desert Eagle (63) | 63 | 252 — instant kill |
| MP5-SD (27) | 27 | 108 — instant kill |
| Glock 17 (28) | 28 | 112 — instant kill |
| Nova (24 × 9) | 216 total | 72 per pellet |
| AWP (115) | **115 — instant kill** | 172 |

So: **any rifle or pistol headshot kills instantly from full health**, and the AWP kills with a
single body shot.

### Step 4 — armour

When a target has armour above zero, incoming damage is split:

- If the hit is a **headshot and the target has no helmet**: 100% goes to health (armour does not
  help at all).
- **Otherwise**: only **50%** of the damage reaches health. The rest is absorbed by armour, and the
  armour pool is drained at **half** the absorbed amount.

When the armour pool is smaller than the damage it would need to absorb, it is consumed completely
and the remaining damage is recalculated so that the total is consistent — armour never "overheals"
you.

Concrete numbers against a target with full 100 armour:

| Weapon | Body shot reaching health | Body shots to kill (100 hp) |
| --- | --- | --- |
| AK-47 (36) | 18 | 6 |
| M4A4 (33) | 17 | 6 |
| Desert Eagle (63) | 32 | 4 |
| MP5-SD (27) | 14 | 8 |
| Glock 17 (28) | 14 | 8 |
| AWP (115) | 58 | 2 |

Armour roughly doubles the number of body shots required, which is exactly why it is worth having.

> **In practice, in v0.1.0, everyone always has armour.** Every respawn grants full armour and a
> helmet automatically (see [known quirks](#18-under-the-hood-known-quirks)). Treat the "unarmoured"
> numbers above as reference, and expect your body shots to do about half the listed damage.

---

## 8. Spread and accuracy

Your bullets do not go exactly where the crosshair points. They are scattered inside a cone whose
half-angle in degrees is:

```
spread = SpreadIdle
       + SpreadMove × clamp(horizontalSpeed / 250, 0, 1)
       + SpreadAir            (only while airborne)
       + bloom                (accumulated from your own recent shots)
then × 0.62 if you are crouching and on the ground
```

The crosshair gap shows this value live, so you can learn to read it rather than memorise numbers.

What this means in practice:

| Action | Effect |
| --- | --- |
| Standing still | Minimum spread — this is how you take long-range shots |
| Walking | About half the movement penalty |
| Running full speed | Full movement penalty |
| Jumping | Adds the weapon's air spread on top of everything else, which is huge |
| Each shot fired | Adds `SpreadBloom` to the cone; it decays over time and resets when you reload |
| Crouching | Multiplies the result by 0.62 |

Example (AK-47): standing still your cone is 0.28°. Running flat out it is 6.78°. Crouched and
still it is 0.17°. Airborne it is at least 16.3° — roughly **58× worse** than standing still.

**The rule that follows from all of this:** stop moving before you shoot. In an emergency, crouch
and shoot. Only fire while moving if your target is close enough that the cone is smaller than they
are.

---

## 9. Recoil and spray control

Recoil is a separate mechanic from spread, and it is the one that separates players.

### How it works

1. Your **first bullet always goes exactly where you aimed** (plus spread). No exceptions.
2. Immediately *after* firing, the game adds a kick — a rotation in yaw and pitch — taken from that
   weapon's authored spray pattern.
3. That kick moves **your actual view**. Your crosshair follows it, because it is your view.
4. To keep your bullets on target you must manually drag the mouse the other way. There is no
   automatic compensation for you.
5. The accumulated offset **recovers on its own** after a short delay with no firing — roughly 0.28
   seconds for most weapons, 0.4 for the AWP. The recovery is reasonably quick, so a burst aimed at
   nothing costs you less than a long spray aimed badly.
6. Your position in the pattern **resets if you stop firing long enough**, so tapping and bursting
   always starts from the accurate first shot.
7. Reloading also resets the pattern index.

The pattern is **authored per weapon**, with a full 30-entry table. Different guns genuinely behave
differently: the AK-47 climbs hard and then sweeps to the side, the M4A4 is flatter and more
horizontal, and the AWP's single-shot kick is enormous but irrelevant because you only fire once.

### How to actually control it

- **Shoot in bursts of 2–4.** The first few bullets are the accurate ones.
- **Pull down as you fire.** The vertical climb is far larger than the horizontal drift, especially
  for the AK. Most of your control effort is just holding the mouse down and back.
- **Learn one gun first.** Take the AK-47 every round and train the first ten bullets of its
  pattern; that covers the vast majority of real fights.
- **Reset between bursts** rather than holding the trigger and hoping, and let the pattern index
  reset so the next burst starts accurate.
- **Crouch.** It does not reduce recoil itself, but it reduces spread, so combined with burst
  discipline it is the difference between hitting and missing at range.
- Watch the crosshair, not the tracers. The crosshair gap tells you both your spread and, together
  with the visible kick, where your next shot is likely to land.

When you die you also get a downward "land dip" on the view model after a fall, which is cosmetic —
but the landing itself does not affect your spread beyond the airborne state going away.

---

## 10. Weapon reference

Every number below is read from the source table in
[`src/Combat/Weapons.cs`](../src/Combat/Weapons.cs).

| Weapon | Class | Slot | Price | Damage | Head × | Falloff | Range | RPM-ish | Mag / Reserve | Reload | Move × | Kill award |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Knife | Melee | 1 | — | 55 | ×3 | — | 90 | 150 | — | — | 1.00 | $1500 |
| Glock 17 | Pistol | 2 | $200 | 28 | ×4 | 0.35 | 1500 | 400 | 20 / 120 | 2.2 s | 1.00 | $300 |
| Desert Eagle | Pistol | 2 | $700 | 63 | ×4 | 0.25 | 2000 | 267 | 7 / 35 | 2.2 s | 1.00 | $300 |
| MP5-SD | SMG | 3 | $1500 | 27 | ×4 | 0.32 | 1800 | 750 | 30 / 120 | 2.6 s | 0.96 | $300 |
| AK-47 | Rifle | 3 | $2700 | 36 | ×4 | 0.25 | 4000 | 600 | 30 / 90 | 2.5 s | 0.90 | $300 |
| M4A4 | Rifle | 3 | $3100 | 33 | ×4 | 0.26 | 4000 | 666 | 30 / 90 | 3.1 s | 0.90 | $300 |
| Nova | Shotgun | 3 | $1050 | 24 × 9 | ×3 | 0.72 | 700 | 68 | 8 / 32 | 3.4 s | 0.95 | $900 |
| AWP | Sniper | 3 | $4750 | 115 | ×1.5 | 0.15 | 6000 | 41 | 10 / 30 | 3.7 s | 0.83 | $100 |

"Move ×" is the multiplier applied to your run speed while that weapon is in hand. The AWP slows
you to 83% of 250 = 208 units/s, so carrying it is a real commitment.

### Per-weapon notes

**Knife** — 55 damage with a ×3 headshot multiplier, and a **$1500 kill reward**, five times a
normal kill. A knife kill is worth more than two rifle kills. It has no spread and no falloff.
The 0.4 s swing means you get one attempt, so take it from behind.

**Glock 17** — Your default. Cheap, fast, 20 rounds, and a ×4 headshot multiplier, which means a
single Glock headshot kills instantly from full health. It is a genuinely dangerous weapon in the
opening seconds of a match, and the falloff (0.35) punishes you past ~1500 units. Fire it as a
semi-auto: the cycle time is 0.15 s so it keeps up with your clicks.

**Desert Eagle** — 63 damage, ×4 headshot, and only 7 rounds with 267 RPM. Against armour it still
does 32 per body shot, which is the best of any pistol. The trade is that it kicks violently and
recovers slowly, so it rewards one careful shot over a flurry. If you hit the first shot you usually
win; if you miss, you are usually dead.

**MP5-SD** — The cheapest automatic, and the only weapon in the game with a movement multiplier
above 0.9. At 750 RPM with a modest movement spread penalty (3.2, against the AK's 6.5) it is the
best weapon for running and gunning. Its damage falls off hard, so treat 1800 units as your limit.

**AK-47** — The reference rifle. 36 damage, 600 RPM, ×4 headshots, a 4000-unit range and only 25%
falloff, so it is lethal everywhere on this map. Its spray pattern is the signature rise-then-sweep:
the first several bullets climb steeply, then it drifts sideways. Turn your sensitivity down and
practice the first ten.

**M4A4** — Slightly less damage (33), slightly faster (666 RPM), a flatter and more horizontal
pattern, a longer reload (3.1 s vs 2.5 s) and $400 more expensive. If you can hold the AK, the AK is
the better buy in v0.1.0. The M4 is more forgiving for newer players because the climb is smaller.

**Nova** — 9 pellets × 24 damage with a ×3 headshot multiplier, but a savage 0.72 falloff: it does
full damage only inside a few hundred units, and roughly 7 damage per pellet at 1000 units. Inside
a room it is a one-shot kill on a body hit; across the map it is a noisemaker. Its $900 kill reward
also makes it a legitimate economy play.

**AWP** — 115 damage with 97.5% effective penetration and only 15% falloff, so it kills with one
body shot at essentially any range on this map. Costs $4750, 41 RPM, 10 rounds, and slows you
badly. Right click toggles the scope (20° field of view). **The weapon is fully stowed while the
scope is up**, so until you unscope you have no fallback. It has the largest view kick in the game,
though with a 1.46-second cycle time that rarely matters.

---

## 11. Economy and buying

### Money rules

| Rule | Value |
| --- | --- |
| Starting money | **$800** |
| Money cap | **$16,000** |
| Normal kill reward | **+$300** |
| Headshot kill bonus | **+$100** on top of the kill reward |
| Knife kill reward | **$1500** |
| Nova kill reward | **$900** |
| AWP kill reward | **$100** |
| Respawn top-up | If you respawn with less than **$1100**, you are topped up to $1100 |

Because the AK-47 costs $2700, a normal kill is worth roughly a ninth of one. At $800 to start you
cannot afford a rifle on the first spawn, so the opening of a match is pistol-only.

The **$1100 respawn floor** means that however badly a run goes, you always come back able to buy
at least an MP5-SD or a Desert Eagle. You can never be permanently locked out of a usable gun.

The **knife's $1500** reward is the biggest economic play in the game: killing a distracted bot with
the knife pays for half an AK-47.

### The buy menu

Press `B` while alive. It contains:

| Item | Price |
| --- | --- |
| Kevlar + Helmet | $1000 |
| Glock 17 | $200 |
| Desert Eagle | $700 |
| Nova | $1050 |
| MP5-SD | $1500 |
| AK-47 | $2700 |
| M4A4 | $3100 |
| AWP | $4750 |

Buying a weapon you already have refills it. **You keep your loadout through death**, so once you
own a rifle you never have to buy it again unless you want a different one.

> **Important v0.1.0 quirk:** every respawn already gives you full armour and a helmet for free, so
> the $1000 "Kevlar + Helmet" purchase does nothing. Spend that money on a weapon instead. See
> [known quirks](#18-under-the-hood-known-quirks).

### When to buy what

- **Under $1500:** Glock 17 (free, you already have it) or Desert Eagle at $700 if you want to win
  pistol fights. Skip the Nova unless the map is forcing you into rooms.
- **$1050–$1500:** MP5-SD. It is the best value in the game for the money and the only gun that
  keeps you mobile.
- **$2700+:** AK-47. This is the default answer and stays the default answer.
- **$4750+ and you are confident:** AWP. It one-shots at any range on this map, but missing your
  first shot usually means dying, and the movement penalty makes you easy to catch.
- **Never spend $1000 on armour** in the current build.

---

## 12. Match rules

| Rule | Value |
| --- | --- |
| Mode | Free-for-all deathmatch, no teams |
| Kills to win | **30** (configurable in the menu) |
| Time limit | **600 seconds** (10 minutes) |
| Respawn delay | **3 seconds** |
| Max bots | 15 |
| Respawn safety | Spawn points are chosen at least **900 units** away from any living player, and are verified to be clear of geometry |
| Spawn state | Full 100 health, 100 armour, helmet, knife and pistol |

The match ends the moment either the kill limit or the clock runs out. Either way you get a result
screen showing your final rank, followed by the option to play again or return to the menu.

---

## 13. Bots

The bots are not simple aim-bots with a fixed skill dial. They perceive, decide and move.

### How they see you

- **View cone:** roughly 65° either side of where they are facing.
- **View range:** between 2200 and 3600 units depending on skill.
- **Hearing radius:** a much shorter radius in which they notice you regardless of facing — this is
  why walking (`Shift`) matters.
- **Line of sight is required.** Every one of their perceptions is confirmed with the same raycast
  the player's bullets use. Hiding behind a crate works on them for exactly the same reasons it
  works on you.

### How they fight

- They react after a **difficulty-dependent delay** before their first shot — this is your window.
- They **turn at a limited rate**, so they cannot snap to a target behind them.
- They apply **aim error** which is larger at close range and smaller far away (they lead less
  precisely up close).
- They fire in **bursts** with pauses between them, rather than holding the trigger forever.
- They **strafe while duelling** and try to hold a preferred engagement range.
- They **take cover when hurt**.
- They compensate for **their own accumulated recoil**, so their barrels genuinely point where they
  are shooting.

### The four tiers

| Skill | Reaction time | Turn rate | Aim error | View range | Preferred range | Burst |
| --- | --- | --- | --- | --- | --- | --- |
| **Easy** | 0.42–0.62 s | 190°/s | 3.40° | 2200 | 800 | 2–5 shots, 0.55–1.40 s pause |
| **Normal** | 0.28–0.44 s | 260°/s | 2.00° | 2600 | 750 | 3–7 shots, 0.40–1.05 s pause |
| **Hard** | 0.18–0.30 s | 340°/s | 1.15° | 3000 | 700 | 4–10 shots, 0.28–0.80 s pause |
| **Expert** | 0.10–0.18 s | 460°/s | 0.60° | 3600 | 650 | 5–14 shots, 0.18–0.60 s pause |

Read this as a difficulty curve: an Expert bot notices you in a tenth of a second, turns faster
than you can track, and misses by about a third of what an Easy bot does. An Easy bot gives you
four tenths of a second to shoot first.

### Exploiting them

- **Easy and Normal bots genuinely lose fair fights** if you pre-aim and shoot first. That reaction
  delay is a real window.
- **They must turn.** Their maximum turn rate is the most exploitable number in the table. If you
  appear behind an Expert bot mid-turn, it cannot face you instantly — the same is true at every
  tier, just faster.
- **Break line of sight.** Because perception needs a raycast, ducking behind a crate resets their
  knowledge. They do not know where you went.
- **Use their preferred range against them.** They hold 650–800 units, so closing to knife range or
  backing off past their view range are both legitimate responses.
- **Listen for their bursts.** The pause between bursts is a free window — that is when you peek.
- **Higher tiers take better guns.** Bot loadouts are weighted by tier, and only the better bots
  ever get the AWP, so a distant one-shot is usually a sign of an Expert.

### Their weaknesses

- Bots never buy, so they cannot upgrade mid-match the way you can.
- Bots have no grenades (there are no grenades in the game yet).
- Bots do not use the knife tactically and will not chase an easy melee kill.
- Bots do not coordinate; this is free-for-all, so they fight each other as much as they fight you —
  a crowded match is often easier than a quiet one.

---

## 14. The map: Crossfire

The arena is **2400 × 2400 units**, enclosed by 300-unit-high walls, with a ring route around a
central building, four full-height corner pillars, and one corridor.

### Layout

```
                        NORTH  (z = +1200)
        +---------------------------------------------------+
        |  [pillar]                       [pillar]           |
        |                                                    |
        |        #######   north divider  #######            |
        |                                                    |
        |   WEST        +-------------------+                |
        |   CORRIDOR    |   CENTRAL         |   EAST         |
        |   (walls,     |   BUILDING (mid)  |   TERRACE      |
        |   one         |                   |   64 units     |
        |   doorway)    +-------------------+   high, stairs |
        |                                                    |
        |  [pillar]                       [pillar]           |
        |                     south pit crates               |
        +---------------------------------------------------+
                        SOUTH  (z = -1200)
```

Coordinates for reference (X runs east–west, Z runs north–south, Y is up):

| Area | Extent | Description |
| --- | --- | --- |
| **Central building ("mid")** | X −380…220, Z −260…120 | Two stacked crate blocks, 128 and 72 units high, with a 40-unit lip on its south face and a raised metal block on top at 128–168 units |
| **West corridor** | X < −820 | Full-height walls with a single **doorway gap at Z −200…400**. Contains a **penetrable** thin wall at Z −900…−600, plus crates |
| **East terrace** | X > 700, Z −600…600 | A 64-unit-high platform with crates on top. Reached from **stepped blocks on its west edge**, 48 → 32 → 16 units high |
| **North divider** | Z −900…−820 | Two 140-unit-high wall segments with a gap between them, plus crates |
| **South pit** | Z 700…1000 | Scattered crates with gaps between them |
| **Corner pillars** | ±620…700 in X and Z | Four 80 × 80 full-height pillars |

### Getting onto the east terrace

This is the one piece of vertical movement worth learning, because the terrace overlooks the whole
east side. It is **64 units** high and your jump apex is **56.25 units**, so you cannot jump straight
up onto it. Instead:

1. From the floor west of the terrace, jump onto the **48-unit block** at X 580–620.
2. Step east and down onto the **32-unit** and then the **16-unit** tread.
3. From the 16-unit tread, jump — 16 + 56 clears the 64-unit lip, and the automatic step-up
   finishes the job.

Coming the other way, simply walk off the terrace; the fall is harmless (fall damage is not
implemented, only a cosmetic landing dip).

### The penetrable wall

Inside the west corridor there is one thin wall marked penetrable, at around Z −900 to −600. You can
shoot through it with reduced damage, and so can the bots. It is the best place on the map to learn
how cover penetration feels — and the worst place to assume you are safe.

### Spawn points

There are 13 spawns: three on the far west (X −1080), three along the north edge, three along the
south edge, two up on the east terrace, and two mid-map. The game picks a spawn at least **900
units** from every living player and verifies it is clear of geometry, so you will not spawn inside
a crate and you will not spawn on top of an enemy.

---

## 15. Tactics and practice drills

### The five habits that matter most

1. **Stop before you shoot.** Moving more than doubles your cone and running multiplies it by 24.
   Even a quarter-second of standing still turns a miss into a kill.
2. **Aim at head height, always.** Since any rifle headshot is an instant kill from full health, and
   armour roughly doubles the body shots required, headshots are worth twice as much as anything
   else you can do. Keep your crosshair at head level as you move, and let enemies walk into it
   rather than flicking up to them.
3. **Burst, don't spray.** Two to four rounds, then reset. The pattern's first shots are the
   accurate ones.
4. **Use the radar as confirmation, not as radar.** It tells you when you are seen. It does not
   tell you where to pre-aim.
5. **Play the corners and the terrace.** The ring route means you can always be flanked, so move
   between cover rather than standing in the open.

### Angle-holding on this map

- The **west doorway** (Z −200…400) is the map's single best choke point: you can hold it from
  inside the corridor and shoot through the penetrable wall at anyone who tries to flank.
- **Mid** rewards crossfire-positioning — hold one side of the central building while bots walk past
  the other.
- The **east terrace** gives you height and a long sightline, but you are also silhouetted, and
  getting down takes time. Do not go up there with three bots alive and one behind you.
- The **north divider** walls break the long north sightline into two shorter ones, which is where
  the AWP loses some of its dominance.

### Practice drills

- **Recoil drill:** stand still, hold the trigger on a wall, and try to keep all 30 rounds inside a
  small square by dragging the mouse down and slightly sideways. Do this with the AK-47 first.
- **Strafe-stop drill:** strafe left and right while keeping your crosshair on a fixed point, then
  practise stopping dead and firing immediately. This is the single most transferable skill.
- **Head-height drill:** walk the ring route without ever letting your crosshair drift above or
  below head level on the crates. Do it with bots set to Easy so you can concentrate on the habit.
- **Corridor drill:** hold the west doorway against `--bots 15` on Expert in a headless match and
  watch the scoreboard accuracy, then try to beat it in-game.

### A note on difficulty

Start on **Normal**. Easy is genuinely easy and will teach you bad habits; Hard and Expert react
faster than most people can process a new target, and are more useful as a test of discipline than
as a fun default.

---

## 16. Command line reference

```
dotnet run [-- option value ...]
```

| Option | Applies to | Default | Meaning |
| --- | --- | --- | --- |
| `--help`, `-h` | — | — | Print usage and exit |
| `--headless` | mode | — | Run a full match with no window and print a report |
| `--screenshot <file>` | mode | `screenshot.png` | Render one real gameplay frame to a PNG |
| `--seconds <n>` | headless | 90 | Simulated seconds to run |
| `--seconds <n>` | screenshot | 4 | Seconds of gameplay before attempting the capture |
| `--bots <n>` | both | 9 | Number of bots |
| `--skill <level>` | both | Normal | `Easy`, `Normal`, `Hard` or `Expert` |
| `--seed <n>` | both | 20260114 | Deterministic RNG seed — the same seed reproduces the same match |
| `--width <n>` | screenshot | 1280 | Window width |
| `--height <n>` | screenshot | 720 | Window height |
| `--weapon <name>` | screenshot | default | Equip a weapon: `Knife`, `Glock 17`, `Desert Eagle`, `MP5-SD`, `AK-47`, `M4A4`, `Nova`, `AWP` |
| `--screen <name>` | screenshot | play | `play`, `scoreboard`, `buy` or `menu` |
| `--zoom` | screenshot | off | Hold the AWP scope before capturing |
| `--verbose` | headless | off | Also dump the kill feed |

### Examples

```bash
# reproduce a specific canonical match, no window
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114

# capture the title screen
dotnet run -- --screenshot shots/00-menu.png --seconds 0 --screen menu

# a scoped AWP frame at 1600x900, waiting 7 seconds of gameplay first
dotnet run -- --screenshot awp.png --seconds 7 --weapon AWP --zoom --width 1600 --height 900
```

The `--headless` report ends in `RESULT: PASS` or `RESULT: FAIL` and exits with a matching status,
so it can be used in a script or a CI job.

---

## 17. Troubleshooting

### "The game starts but there is no window"

Check the console output. If it exits immediately with an exception, the graphics device probably
failed to initialise. Make sure your GPU drivers are current and that nothing else has exclusive
fullscreen control.

### The built `.exe` says it cannot find the runtime

If you run `bin/Debug/net10.0/HoodGlobalOffensive.exe` directly and the SDK was installed
per-user — or anywhere outside the system `PATH` — set `DOTNET_ROOT` first:

```bash
# Git Bash / Linux / macOS
export DOTNET_ROOT="$HOME/.dotnet"

# Windows cmd
set DOTNET_ROOT=%USERPROFILE%\.dotnet
```

`dotnet run` does not need this. Neither does `play.cmd`, which sets it for you.

### "Access is denied" / `FileLoadException` when building Release

Some Windows machines have an application-control policy that blocks newly built executables from
running. If `dotnet run -c Release` fails with a `FileLoadException` while the default Debug
configuration works, that policy is the cause, not the code. Use the default configuration:

```bash
dotnet run                      # this is Debug, and it works
```

### No sound

All audio is generated at startup and played through raylib, so there are no audio files to go
missing. If you hear nothing: confirm the game has focus (audio may be suspended when the window is
unfocused), check your default output device, and check the system volume mixer for the game's own
channel.

### The mouse cursor will not come back

Press `Esc` to pause, which releases the cursor. Closing the pause menu recaptures it.

### The window is too small or the wrong size

For gameplay, resize the window freely — the renderer adapts. For screenshots the size comes from
`--width` and `--height`.

### The game feels too sensitive (or not sensitive enough)

Change **MOUSE SENSITIVITY** in the main menu. The conversion is
`degrees per mouse count = sensitivity × 0.04`, so sensitivity 2.2 gives about 0.088° per count.
To bake in a new default, edit `Cfg.Sensitivity` in [`src/Core/Cfg.cs`](../src/Core/Cfg.cs).

### Performance

The simulation is bound by tick rate rather than rendering, and the headless harness runs at
roughly 3,600 ticks per second on a mid-range desktop, which is about 60× real time. If you want
fewer moving parts, reduce `--bots`.

---

## 18. Under the hood: known quirks

These are things the code does that a player should know about, because they change how the game
plays. All of them are tracked in [ROADMAP.md](ROADMAP.md).

| Quirk | Effect on play |
| --- | --- |
| **Armour is free on every respawn.** `Simulation.Respawn` sets armour to maximum and the helmet flag to true for everyone. | The $1000 "Kevlar + Helmet" item in the buy menu is dead money. Everyone you fight has armour, so body shots do about half their listed damage. |
| **`WeaponDef.ArmorPenetration` is never read.** Each weapon defines an armour-penetration ratio (0.47 for the Glock, 0.975 for the AWP) and nothing consumes it; armour uses a flat 50% rule instead. | The AWP's armour-penetration advantage over the Glock is currently theoretical. Both face the same 50% reduction. |
| **There is no stomach hitbox.** `HitGroup.Stomach` exists and carries a 1.25× multiplier, but no hitbox is ever assigned to it. | Body damage is uniform. There is no "shoot the belly for more damage" trick. |
| **`WeaponDef.LegMultiplier` is never read.** Leg damage uses a hardcoded 0.75. | None — the field is simply redundant dead data. |
| **No fall damage.** | Jumping off the east terrace is free. |
| **No grenades, no bomb, no teams, no networking.** | Deathmatch only. |

---

## 19. FAQ

**Is this a Counter-Strike clone?**
It is an original game that deliberately reimplements Counter-Strike's *movement and gunplay
physics* — the acceleration model, the spray patterns, the economy — on its own engine and its own
map. It shares no code or assets with Counter-Strike. See the licence note in the
[README](../README.md#license).

**Why does my gun sometimes not shoot?**
You are reloading, your magazine is empty, or you are inside a weapon's cycle time. The buy menu
and pause menu also swallow input while open.

**Why did my shot not kill?**
Almost always one of: you hit the body rather than the head against an armoured target, the
distance falloff reduced the damage, or a wall stopped the bullet — only one thin wall on the map is
penetrable. Check the hitmarker: if you got one, you hit; the damage numbers tell you how much.

**Why can't I see anyone on the radar?**
Because you cannot see them. The radar only shows enemies you currently have line of sight to.

**Can I play with friends?**
Not in v0.1.0. There is no networking at all — everything runs in one process.

**How do I add a map?**
The map is pure data: `GameMap.cs` builds a list of axis-aligned brushes with a single `Add(...)`
call each, and the navigation graph for the bots is sampled from that geometry at startup. See
[ARCHITECTURE.md](ARCHITECTURE.md).

**How do I change gravity, run speed, or how hard the bots are?**
Everything is in one table: [`src/Core/Cfg.cs`](../src/Core/Cfg.cs) for physics and match rules,
[`src/Combat/Weapons.cs`](../src/Combat/Weapons.cs) for per-weapon numbers including the spray
patterns, and `BotProfile.For` in [`src/Entities/Bot.cs`](../src/Entities/Bot.cs) for the four
difficulty tiers.

**How do I know I haven't broken something?**
Run `dotnet run -- --headless --seconds 300 --bots 9`. It plays a whole match with a scripted player
and asserts the invariants, so it will tell you if movement, combat or respawning has regressed.
