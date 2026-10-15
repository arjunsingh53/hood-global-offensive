# HANDOFF — Hood Global Offensive

**Written:** 2026-10-15 · **Version:** v0.1.0 (early access) · **Status:** playable, documented, documented-verified, **not yet pushed to GitHub**

This document is a complete, self-contained briefing. It assumes you have never seen this project.
Read it top to bottom before touching anything — sections 2 and 8 in particular will save you hours.

---

## 1. What this is

A **Counter-Strike style free-for-all deathmatch FPS written from scratch in C#** on top of the thin
[Raylib-cs](https://github.com/ChrisDill/Raylib-cs) binding. No game engine: all level geometry is
code and every sound effect is synthesised to WAV at startup.

- **25 source files, 5,873 lines of C#** (plus 795 lines of Python/shell verification tooling).
- **The one binary file in the project is a weapon mesh** — `assets/weapons/glock17.glb`, 2.7 MB, the
  Glock 17's first-person model. It is generated, not hand-committed: the recipe is
  `tools/build_weapon_asset.py` and the source asset is 145 MB and stays out of the repo. See §6 for
  the two raylib constraints that decide what that file may contain.
- One map ("Crossfire"), one mode (free-for-all deathmatch), 8 weapons, up to 15 bots.
- Builds clean: **0 warnings, 0 errors**.
- **49 files tracked in git, 2 commits**, branch `main` (commit 1 = the game, commit 2 = this handoff).

### Where it lives

```
C:\Users\suref\.config\freebuff-desktop\no-folder\threads\9fad2381963091d2d4fa198640d7f295d87ad2cc728d28a6217080973590bde2
```

> ⚠️ **This path is a Freebuff chat-thread workspace and is not a permanent home.** Move the folder
> somewhere normal *first* (e.g. `C:\Users\suref\Documents\HoodGlobalOffensive\`). Nothing is
> hardcoded to the current path — it is a plain .NET project with relative build paths, so moving it
> and running `dotnet build` is all that is required. The local git repo moves with it, including its
> commits.

---

## 2. The five things that will bite you

1. **The .NET SDK is NOT on the system `PATH`.** It was installed per-user into `~/.dotnet`. Every
   command in this document therefore starts with:
   ```bash
   export PATH="$HOME/.dotnet:$PATH"
   export DOTNET_ROOT="$HOME/.dotnet"
   ```
   `DOTNET_ROOT` is required to run the built `.exe` directly. `dotnet run` works with just the
   `PATH` part. On Windows, `play.cmd` handles both.

2. **The Release configuration is blocked on this machine.** A Windows application-control policy
   rejects newly built Release executables with `FileLoadException`. This is a machine policy, not a
   code defect. **Verify with the default (Debug) configuration.** Do not chase this as a bug, and do
   not switch to Release expecting it to work.

3. **The simulation/presentation split is load-bearing.** The headless test harness, the
   determinism, and the screenshot tooling all depend on it. Do not let simulation code read input or
   draw. See section 6.

4. **Do not weaken a verification check to make something pass.** The checks in `tools/` encode real
   bugs that were found and fixed. `tools/verify_shots.py` in particular contains a deliberate
   positive control — see section 9.

5. **GitHub publishing is unfinished** and is blocked on an interactive browser authorization that
   only the human can complete. Full state and remaining steps are in section 5.

---

## 3. Current verified state

Everything in this section was actually executed and observed on 2026-10-15. Reproduce it with the
commands given; do not take it on faith.

| Check | Command | Result |
| --- | --- | --- |
| Build | `dotnet build --nologo -warnaserror` | **PASS** — 0 warnings, 0 errors |
| Full match, default settings | `dotnet run --no-build -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114` | **PASS** — 29/10 K/D, 34.9% accuracy, 0 geometry violations, kills == deaths, RESULT: PASS |
| Crowded match, Hard | `dotnet run --no-build -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331` | **PASS** — 8/3 K/D, 15 actors, all invariants hold |
| Screenshot artifacts | `python tools/verify_shots.py` | **ALL PASS** — 7 artifacts (the Glock 17 frame was added) |
| All 8 weapon view models × 5 moments | `python tools/sweep_weapons.py` | **ALL PASS** — worst case Glock 17 at 5.9% of the lower-right region, max 2.4% in the centre, max 0.4% in the lower-left |
| Documentation links | custom walker over all `.md`/`.html` | **57 links, 0 broken** |
| Interactive game launch | `dotnet run` (real window, killed after 10 s) | **PASS** — 1280×720 window on the RTX 3060, raylib 6.0 / OpenGL 3.3 / MSAA×4 / VSYNC, audio device up (miniaudio | WASAPI, 48 kHz stereo), 66 synthesised WAVs loaded, view-model framebuffer created, still alive after 10 s, zero exceptions |

### Git state

```
7fd1217 Add HANDOFF.md: complete briefing for a new agent taking over the project
7e3a551 Initial commit: Hood Global Offensive v0.1.0 (early access)
branch: main
49 tracked files, 2 commits
remote: none  ← NOT PUSHED
working tree: 17 modified files + 3 new paths, all the Glock 17 mesh work, UNCOMMITTED
```

The uncommitted change set is: the art mesh (`assets/weapons/glock17.glb`), its loader
(`src/Render/WeaponModels.cs`), its build pipeline (`tools/build_weapon_asset.py`), the view-model and
weapon-name changes in `src/`, the regenerated `shots/`, and the documentation updates listed in
`CHANGELOG.md` under `[Unreleased]`. It was verified the same way the rest of the project is — build,
headless match, `verify_shots.py`, `sweep_weapons.py` — and nothing in it is speculative.

`bin/`, `obj/`, `__pycache__/`, and capture output are gitignored. Verify with
`git status --short --ignored` before committing that this is still true.

---

## 4. Build, run and verify

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

dotnet build --nologo -warnaserror    # must stay at 0 warnings
dotnet run                            # play the game
dotnet run -- --help                  # every command-line option

# the single most useful check in the project:
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114
```

The headless run plays a **complete match with a scripted robot player, with no window and no GPU**,
and asserts invariants. This is how gameplay regressions get caught. It exits non-zero on failure
and prints a report ending in `RESULT: PASS` or `RESULT: FAIL`.

Invariants it asserts:

- the player never ends up embedded in level geometry
- no transform ever becomes non-finite
- recorded kills equal recorded deaths
- every chosen spawn point is clear of geometry
- the match actually produced shots and kills *(this last one matters: it is the difference between
  "it ran" and "it worked")*

### Visual verification

```bash
bash tools/capture_shots.sh        # regenerates all 6 screenshots from real matches
python tools/verify_shots.py       # asserts framing, scope mask, overlays  -> non-zero on failure
python tools/sweep_weapons.py      # all 8 view models at 5 moments each     (~1 minute)
```

`tools/verify_shots.py` decodes the PNGs with the Python standard library only (no Pillow) and
measures real pixel statistics. Run it after **any** change to `Renderer.cs`, `Hud.cs` or
`Screens.cs`.

See [docs/BUILDING.md](docs/BUILDING.md) for publishing and distribution commands.

---

## 5. IMMEDIATE PENDING TASK — publish to GitHub

**Goal:** create `github.com/arjunsingh53/hood-global-offensive`, **public**, and push `main`.

**Decided and confirmed by the human:** repo must be public; screenshots included in the README;
repo name comes from "HOOD GLOBAL OFFENSIVE (FPS)". GitHub names cannot contain spaces or
parentheses, so the slug is `hood-global-offensive`.

### Current blocker

`gh auth login` requires the human to enter a one-time code at <https://github.com/login/device>.
Two attempts were made; the *first* failed because gh was started with `&` inside a synchronous
command and was killed when that command finished, so the authorization the human completed had
nowhere to land. The second attempt was started as a genuine persistent background process and was
still waiting at the time of writing.

**The lesson: `gh auth login` must run as a persistent background process (`process_type:
BACKGROUND`), not with `&` inside a SYNC command.** If the flow has died, restart it that way, read
the new code from the log, and have the human enter it.

### gh CLI is already installed — user-scope, no admin needed

```
C:\Users\suref\.local\gh\bin\gh.exe        # gh 2.102.0
```
It was installed by downloading the release zip and extracting it, deliberately avoiding the MSI /
winget path so that no elevation was required. It is **not** on `PATH`; call it by full path or add
it.

### Steps to finish

```bash
export PATH="$HOME/.dotnet:$PATH"
GH="$HOME/.local/gh/bin/gh.exe"

# 1. confirm identity
"$GH" auth status
"$GH" api user --jq '.login'          # expect: arjunsingh53

# 2. create the repo and push in one step (run from the project root)
"$GH" repo create hood-global-offensive \
  --public \
  --source=. \
  --remote=origin \
  --push \
  --description "Early access Counter-Strike style FPS written from scratch in C# on Raylib-cs - one map, bots, buy economy, no game engine, generated assets."

# 3. verify it actually landed
"$GH" repo view arjunsingh53/hood-global-offensive --web
git remote -v
git log --oneline origin/main
```

### Then, still to do

- **Tag the release:** `git tag -a v0.1.0 -m "..." && git push origin v0.1.0`, then
  `gh release create v0.1.0 --title "v0.1.0 - early access" --notes-file <notes>`. Mark it as a
  **pre-release** — this is explicitly an early access project, and the README says so prominently.
- **Check the README renders.** Open the repo page and confirm the badge row, the screenshot gallery
  and every relative link resolve. The images are referenced as `shots/*.png`, which is correct for
  the GitHub renderer.
- **Check the CI workflow ran.** `.github/workflows/verify.yml` runs the build and both headless
  matches on push. It was validated locally (all three steps pass) but has never run on a GitHub
  runner.
- **Add repo topics** so it is discoverable: `game`, `fps`, `csharp`, `dotnet`, `raylib`,
  `counter-strike`, `gamedev`, `early-access`.

### Things deliberately NOT done

- **No force-push, no history rewriting after publication.** The history is a single clean initial
  commit on purpose.
- **The repo is public**, which the human explicitly confirmed.

---

## 6. Architecture in one page

> **The one rule: the simulation never reads input and never draws. The presentation never changes
> game state.**

This is what makes a windowless, GPU-less full-match test possible — and that test is what found
most of the real bugs in this project. Do not break it.

```
                ┌──────────────────────────────────────┐
                │   Simulation  (pure state + logic)   │
                └────────┬───────────────────┬─────────┘
                         │ InputFrame        │ reads state
        ┌────────────────┴──────┐   ┌─────────┴──────────────────────┐
        │ RealInput (keyboard)  │   │ Renderer / Hud / Screens       │
        │ ScriptedInput (robot) │   │ AudioBank                      │
        └───────────────────────┘   └────────────────────────────────┘
```

| Directory | Responsibility |
| --- | --- |
| `src/Core` | `Cfg.cs` (**every** gameplay constant in one table), `Util.cs` (maths + deterministic xorshift `Rng`), `Input.cs` (`InputFrame` / `RealInput` / `ScriptedInput`), `Game.cs` (window, state machine, frame loop, owns the view-model render target) |
| `src/World` | `Geometry.cs` (`Aabb` only), `GameMap.cs` (the arena as data + all world queries), `CharacterMover.cs` (Quake/CS physics, swept collision, step-up), `NavGraph.cs` (grid-sampled waypoint graph + A*, 895 nodes, auto-derived from the geometry) |
| `src/Combat` | `Weapons.cs` (`WeaponDef` table incl. 30-entry spray patterns, `Weapon` runtime state), `Hitscan.cs` (one function that turns a ray into a `ShotResult`; shared by player and bots) |
| `src/Entities` | `Actor.cs` (shared state + `ApplyDamage`), `Player.cs` (view angles, weapon handling, view-model animation state), `Bot.cs` (perception, state machine, duelling, `BotProfile` difficulty tiers) |
| `src/Sim` | `Simulation.cs` (the authoritative world), `Effects.cs` (tracers, particles, decals, damage numbers, kill feed), `HeadlessRunner.cs` (scripted match runner + invariant assertions) |
| `src/Render` | `Renderer.cs` (3D world, characters, view model), `ScreenshotCapture.cs` (renders one real frame) |
| `src/UI` | `Ui.cs`, `Hud.cs`, `Screens.cs` (immediate-mode drawing, HUD, menus, buy menu, scoreboard) |
| `src/Audio` | `WavGen.cs` (procedural WAV synthesis), `AudioBank.cs` (bank + distance attenuation) |

Full reasoning, including the render-target trick that stops walls clipping the weapon, is in
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

### Design decisions you must not casually undo

- **The world is entirely axis-aligned boxes.** This makes collision trivial and lets the navigation
  graph be *derived* from geometry rather than authored by hand. Adding a crate is one line and bot
  pathing updates itself. Diagonal geometry is impossible by design.
- **Recoil is applied *after* the shot**, so the first bullet always flies true — this is the single
  most important gunplay property.
- **The view model renders into its own render target** with the camera pinned to the origin. It
  therefore cannot be occluded by a wall. `Rlgl.DisableDepthTest()` was tried first and **does not
  work** through raylib's batched renderer; do not go back to it.
- **`Rlgl.SetClipPlanes` is called explicitly** per pass. raylib's default 1000-unit far plane is far
  too small for a 2400-unit arena. If distant geometry vanishes, this is why.
- **All audio is synthesised at runtime**, and the one binary asset is a weapon mesh whose recipe is
  checked in. If you add art, it goes through `tools/build_weapon_asset.py`; do not commit a raw
  artist export.
- **Weapon meshes must be PNG-textured and un-quantized.** Both are measured properties of this
  raylib build, not preferences: embedded JPEG images fail to decode (a JPEG-textured GLB loads with
  a 1×1 white texture in every slot, which is what silently happens before you notice), and
  gltfpack's `KHR_texture_transform` UV quantization is ignored by the loader, which would leave the
  mesh sampling a sixteenth of its albedo. `tools/build_weapon_asset.py` documents both.
- **The art weapon's pose is measured, not hard-coded.** `src/Render/WeaponModels.cs` derives the
  scale and centre from the mesh bounding box and the bore line from the centroid of its front 2%.
  Do not replace that with literals copied from a screenshot; re-exporting the asset would then break
  the framing silently.
- **View-model animation is tuned per weapon shape.** The box weapons are far chunkier than a real
  pistol, so `WeaponArtwork.ReloadSwing` scales the reload flourish down for meshes; at the full angle
  the Glock 17 leaves the frame. The weapon sweep measures this (§9).

---

## 7. Where the interesting code is

| What | File |
| --- | --- |
| Every gameplay constant (gravity, speeds, damage rules, match rules) | `src/Core/Cfg.cs` |
| Weapon stats + the 30-entry spray patterns + recoil recovery | `src/Combat/Weapons.cs` |
| The damage pipeline: falloff, hitgroup multipliers, armour, penetration | `src/Combat/Hitscan.cs` + `src/Entities/Actor.cs:135` (`ApplyDamage`) |
| Movement physics and collision | `src/World/CharacterMover.cs` |
| The map (a list of `Add(x0,x1,y0,y1,z0,z1,kind,penetrable)` calls) | `src/World/GameMap.cs` |
| Bot difficulty tiers | `src/Entities/Bot.cs:51` (`BotProfile.For`) |
| The match tick loop, scoring, respawning | `src/Sim/Simulation.cs` |
| Invariant assertions | `src/Sim/HeadlessRunner.cs` |
| Screenshot capture states (`play`/`scoreboard`/`buy`/`menu`/`--zoom`) | `src/Render/ScreenshotCapture.cs` |
| Art-mesh weapon loading, measurement and drawing | `src/Render/WeaponModels.cs` |
| The weapon-to-mesh table and the view-model poses | `src/Render/Renderer.cs` (`Artwork`, `PoseFor`) |
| Turning a 145 MB source weapon model into the shipped asset | `tools/build_weapon_asset.py` |
| CLI flag parsing | `Program.cs` |

---

## 8. Known defects and dead code — the highest-value work available

These are **verified** findings, with exact locations. They are the fastest way to make a real
improvement. Everything here is also listed in the README's "Known issues" and in
[docs/ROADMAP.md](docs/ROADMAP.md); the README and the player guide deliberately document the
*actual* behaviour rather than the intended behaviour, so **if you fix one of these, update the
README, `docs/GUIDE.md` §18 and `CHANGELOG.md` in the same commit.**

### 8.1 `WeaponDef.ArmorPenetration` is dead — defined, assigned, never read

- Declared: `src/Combat/Weapons.cs:24`
- Assigned per weapon: `src/Combat/Weapons.cs:160,171,183,196,207,230,253,265` (values 0.47 → 0.975)
- **Read: nowhere.** `grep -rn "ArmorPenetration" src/` returns only the definition and the
  assignments.

Meanwhile `Actor.ApplyDamage` (`src/Entities/Actor.cs:135-163`) uses a **flat** rule: 50% of damage
reaches health through armour, or 100% on a headshot against a target without a helmet.

**Consequence:** the AWP's 0.975 penetration and the Glock's 0.47 are identical in effect. This is
the largest gameplay divergence from Counter-Strike in the project.

**Fix direction:** use the per-weapon ratio to split damage between health and armour, so
high-penetration weapons genuinely punish armour. This is roadmap item #1.

### 8.2 Armour is granted free on every respawn, making a buy-menu item dead money

- `src/Sim/Simulation.cs:419-420` — `Respawn` sets `actor.Armor = Cfg.MaxArmor` and
  `actor.HasHelmet = true` for every actor.
- `src/UI/Screens.cs:242` sells "Kevlar + Helmet" for $1000, which `Screens.cs:329-332` applies.

**Consequence:** the $1000 purchase does nothing, and every fight in the game happens against a
100-armour target. `docs/GUIDE.md` documents this as a quirk and prints both armoured and unarmoured
damage tables because of it.

**This is a design decision, not a pure bug** — removing the free grant may leave new players
defenceless at $800 starting money. Decide the economy before changing the code.

### 8.3 `HitGroup.Stomach` is unreachable

- Enum member: `src/Entities/Actor.cs:13`
- Multiplier: `src/Entities/Actor.cs:139` (1.25×)
- **No hitbox is ever assigned to it.** `Hitscan.TestActors` (`src/Combat/Hitscan.cs:141-143`) assigns
  only `Head`, `Chest` and `Leg`.

**Consequence:** body damage is uniform. Splitting the torso into chest and stomach would make the
existing multiplier live.

### 8.4 `WeaponDef.LegMultiplier` is dead

- Declared: `src/Combat/Weapons.cs:26`
- Read: nowhere. `Actor.cs:139` hardcodes `0.75` instead.

Harmless redundancy, but two sources of truth for the same number is a trap.

### 8.5 Smaller gaps

- **No fall damage** — jumping off the 64-unit east terrace is free.
- **No sound occlusion** — gunfire transmits through walls with distance attenuation only.
- **No settings persistence** — main-menu values reset every launch (roadmap item #4).
- **No key rebinding** — the controls in `Game.cs` are hardcoded.
- **Bots never buy** — they are handed loadouts, weighted by difficulty tier.
- **No grenades** (this is the biggest missing piece of Counter-Strike's tactical vocabulary).
- **One map, one mode, no networking.**

---

## 9. The verification contract

**Do not weaken, skip or loosen a check to make a change pass.** The checks in `tools/` each encode a
specific bug that was found and fixed:

| Check | Bug it was written to catch |
| --- | --- |
| `verify_shots.py` framing | The view model rendered as a thin sliver with its muzzle sitting **on the crosshair** |
| `verify_shots.py` scoped frame | A stowed weapon left a **ghost of its previous frame** composited over the scope |
| `verify_shots.py` title screen | The game was renamed to a 22-character title; this asserts the rendered text is not clipped by the window edge, measured on real pixels rather than assumed from font metrics |
| `sweep_weapons.py` (8 weapons × 5 moments) | The reload animation rotated the anchor frame, swinging the weapon **up** into view instead of dipping it down |
| `sweep_weapons.py` (current run) | The reload flourish swung the art-mesh Glock 17 almost out of frame — 1.0% coverage mid-reload against 9.3% idle — because the animation was tuned for the far chunkier box weapons |
| Headless invariants | `Actor.Deaths` was never incremented — every scoreboard would have shown zero deaths permanently |

Three subtleties to preserve if you touch these checks:

1. **The scoped-frame check uses a brightness *ratio*, not colour matching.** Colour matching was
   tried and abandoned, because the scope mask alpha-blends grey level geometry down into the same
   dark blue-grey band as the gun, producing false positives. The measured false hits were in the
   centre column on the circle's edge blend. Do not "simplify" this back to a colour predicate.
2. **`verify_shots.py` contains a deliberate positive control** (`check_stowed_control`): the same
   statistic that proves a weapon is *stowed* in the scoped frame is measured on a frame whose weapon
   *is* drawn, to prove the check is capable of detecting a gun at all. A check that cannot fail is
   not a check. Keep it.
3. **The framing checks recognise two kinds of weapon.** Box weapons are identified by their exact
   flat colours; the art-mesh Glock 17 is identified by shape (`GUN_DARK`: dark and near-neutral),
   because it is a photo texture. That family is capped at `max(r,g,b) <= 85`, which is *below* the
   world's darkest flat surface — a metal brush's skirt at `(75, 81, 88)` — and every region stops at
   82% of the width because the HUD's radar panel is dark grey as well. If you widen the cap or the
   regions, level geometry and the HUD start counting as a weapon. Measured on a control frame with
   the view model suppressed: **0.0%** inside the checked regions, against 11.6% with it drawn.

### If you believe a check is genuinely miscalibrated

Say so with the measurement that proves it, and change the check **in a way that still fails for the
bug it was written to catch**. That is exactly what happened once before: the original AK-47 framing
threshold was miscalibrated, and the fix was a better measurement, not a relaxed threshold.

### Adding a check

Follow the existing pattern:
1. Make it measure the **shipped artifact**, not intermediate state.
2. Make it able to fail — include a positive control if the check is of the form "X is absent".
3. Exit non-zero on failure.
4. Wire it into `.github/workflows/verify.yml` if it needs no GPU.

---

## 10. Conventions

- File-scoped namespaces; nullable reference types and implicit usings enabled.
- One type per file, named after the file.
- XML doc comments on public and non-obvious members, explaining **why**, not restating the signature.
- **Every tunable number lives in a table** — `Cfg.cs`, the `WeaponDef` list, or `BotProfile.For`.
  Never inline a magic number at the point of use.
- No LINQ or allocations in the hot simulation loop.
- Plain, direct C#. No cleverness without a comment justifying it.
- The build must stay at **0 warnings** (`-warnaserror` is enforced in CI).
- Comments and documentation are in English.

---

## 11. Roadmap, in the order it should be done

Full reasoning in [docs/ROADMAP.md](docs/ROADMAP.md).

**v0.2.0 — fix what the code already promises** (correctness, not features)
1. Wire up armour penetration (§8.1) — the data is already in the weapon table.
2. Decide and implement the armour economy (§8.2).
3. Add a stomach hitbox (§8.3).
4. Settings persistence to a config file.

**v0.3.0 — depth**
5. Grenades (HE + flash first).
6. A second, tighter map — the architecture already supports it and would make the shotgun and SMG
   real choices.
7. Key rebinding.
8. In-game settings screen (resolution, FOV, volume).

**v0.4.0 — modes**
9. Team deathmatch (mostly scoring + spawn selection).
10. Bomb defusal (large; depends on armour and economy being settled first).

**Later:** sound occlusion, bot buying, a replay/demo system (nearly free given the deterministic
simulation + `InputFrame` abstraction), networking, bot coordination.

**Explicitly not planned:** a level editor (the map is code, deliberately), moving to a game engine,
or committing raw art exports — anything binary must arrive with the script that regenerates it.

---

## 12. Environment notes for this machine

| Thing | Detail |
| --- | --- |
| .NET SDK | Installed per-user at `~/.dotnet` (v10.0.402). **Not on `PATH`.** |
| GPU | RTX 3060, raylib 6.0, OpenGL 3.3, MSAA×4 — the game renders correctly |
| Release builds | **Blocked** by a Windows application-control policy (`FileLoadException`). Use Debug. |
| gh CLI | `~/.local/gh/bin/gh.exe` (v2.102.0), installed from the release zip to avoid needing admin. Not on `PATH`. |
| Python | 3.14. `tools/inspect_png.py`, `verify_shots.py` and `sweep_weapons.py` are stdlib-only (the PNG decoder is hand-written); `tools/build_weapon_asset.py` additionally needs Pillow (`python -m pip install --user pillow`) and a `gltfpack` binary if you regenerate a weapon mesh |
| bash | Git Bash |
| Freebuff preview panel | Compositing is unreliable in this environment and often returns "no frames". It was NOT used for verification; the Python PNG measurements were used instead, and they are more trustworthy. Do not try to verify visuals through the preview panel — use `tools/inspect_png.py`. |

`play.cmd` in the project root is a Windows double-click launcher. It sets `DOTNET_ROOT`, changes to
the right directory, passes arguments through to the game, and holds the console open on failure so
the error is readable.

---

## 13. Honest limitations — what has NOT been verified

State these as limitations, not as passes:

1. **Feel is untested.** Mouse sensitivity, recoil weight, and whether Hard bots are fun or
   infuriating are personal-preference judgements that no automated check covers. All of those
   numbers are in `Cfg.cs` and `Weapons.cs` and are quick to tune after playing a round.
2. **The CI workflow has never run on GitHub.** Its three steps were executed locally and all pass,
   but no GitHub runner has been exercised. The workflow deliberately contains only GPU-free checks.
3. **The interactive path was smoke-tested, not played.** A 10-second run of the real `Game.Run()`
   path (re-run *after* the rebrand) confirmed the window opens, audio initialises and the loop is
   stable on this GPU with no exceptions. Nobody has yet played a full match with a keyboard and
   mouse and reported whether it is fun.
4. **Nothing has been pushed.** No remote exists (see section 5).
5. **`sweep_weapons.py` needs a real GL context**, so it cannot run in plain CI as written.
6. **The Glock 17 mesh has been verified by measurement, not by play.** It is framed correctly at five
   different moments in a match and its reload pose stays in frame, but nobody has yet played a round
   with the real asset on screen and judged whether a photoreal pistol sitting next to box-drawn
   opponents looks right.

---

## 14. Suggested first tasks for a new agent

In priority order:

1. **Finish the GitHub push** (section 5). It is the only incomplete deliverable, it is blocking, and
   it needs one browser interaction from the human.
2. **Move the project out of the thread workspace** (section 1) and re-verify the build. Everything
   else is stacked on top of this path.
3. **Wire up armour penetration** (§8.1). The data already exists in the weapon table; this is
   contained, high-impact, and immediately testable through the headless harness plus a damage table
   update in `docs/GUIDE.md` §7.
4. **Add settings persistence** (§8.5) — small, self-contained, and removes a daily annoyance.
5. **Add grenades.** The largest genuinely new capability that clearly fits the existing design, and
   the biggest gap between this game and the one it is modelling.

---

## 15. Quick reference — the numbers that define the game

Everything below is authoritative and read from `src/Core/Cfg.cs` unless noted.

| Quantity | Value |
| --- | --- |
| World units | CS-style: standing player 72 tall, runs at 250 u/s |
| Arena | 2400 × 2400, walls 300 high |
| Run / walk / crouch speed | 250 / 130 / 85 u/s |
| Jump velocity / gravity | 300 u/s / 800 u/s² → **jump apex 56.25 units** |
| Step-up height | 20 units (walk) |
| Air acceleration cap | 30 u/s per tick (this is what makes strafe-jumping work) |
| Friction / stop speed | 5.2 / 80 |
| Max health / armour | 100 / 100 |
| Kills to win / match time | 30 / 600 s |
| Respawn delay | 3 s |
| Start money / cap | $800 / $16,000 |
| Kill reward / headshot bonus | $300 / +$100 (knife $1500, Nova $900, AWP $100) |
| Respawn money floor | top up to $1,100 |
| FOV / default sensitivity | 90° / 2.2 (= 0.088°/mouse count) |
| Nav graph | 895 nodes, 36 map brushes |
| Bots | default 9, max 15, 4 tiers |

The full damage model — exponential falloff per 1000 units, ×4 headshot multipliers, 50% armour
split — is derived with worked tables in [docs/GUIDE.md](docs/GUIDE.md) §7 and §8.

---

## 16. Document map

| File | Contents |
| --- | --- |
| [README.md](README.md) | Public landing page: early-access warning, screenshots, features, quick start, verification evidence, known issues |
| [docs/GUIDE.md](docs/GUIDE.md) | **Full player guide** — controls, HUD, movement tech, exact damage/armour maths, spray control, all 8 weapons, economy, bot tiers, map callouts, tactics, CLI reference, troubleshooting, quirks, FAQ |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Code organisation and the reasoning behind each major decision |
| [docs/BUILDING.md](docs/BUILDING.md) | Build, publish, distribution, and the verification tooling |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Planned work with reasoning |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Architecture rules, verification requirements, PR etiquette |
| [CHANGELOG.md](CHANGELOG.md) | v0.1.0 release notes, including the bugs found during development |
| [LICENSE](LICENSE) | MIT. **Copyright is set to "Arjun Singh (arjunsingh53)"** — confirm or correct the legal name. Also contains an explicit non-affiliation note regarding Valve. |
| `gallery.html` | Standalone screenshot gallery, references `shots/*.png` relatively |

---

## 17. One last thing

The README, the player guide and the changelog **describe what the code actually does**, including
the parts that are wrong (see §8). This was a deliberate choice: an earlier draft documented the
*intended* Counter-Strike behaviour, and writing the guide from the source revealed three mechanics
that were described but not implemented.

Keep that property. If you change behaviour, change the documentation in the same commit — and if you
discover another discrepancy like §8.1, document it as a known issue rather than quietly restating
the claim.
