# Contributing

Thanks for taking a look. This is an early access project and contributions — bug reports,
balance feedback, and pull requests — are all welcome.

By taking part you agree to keep the discussion civil and focused on the work. There is no formal
code of conduct document; the expectation is simply that you treat other contributors decently.

---

## Getting set up

```bash
git clone https://github.com/arjunsingh53/hood-global-offensive.git
cd hood-global-offensive
dotnet run
```

You need the .NET SDK 8.0 or newer (the project targets `net10.0`) and a GPU/display. Full
details are in [docs/BUILDING.md](docs/BUILDING.md).

---

## Before you open a pull request

Run both headless matches — they are the CI gates, and they exit non-zero if an invariant breaks:

```bash
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114
dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331
```

You are looking for `RESULT: PASS`, and for the invariant block to report:

```
ticks with player embedded in geometry : 0
frames with non-finite transforms      : 0
frames with over-extended grapple rope : 0
kills recorded == deaths recorded      : yes
spawns are clear of geometry           : yes
upgrade stations exist                 : yes
```

If you touched a map, run the harness on it directly (`--map <name>`), and if you touched anything
visual, capture a frame from the relevant staged screen (`--screenshot shot.png --screen <name>`).
Captures need a GPU; if you cannot run one, say so in the PR rather than leaving it ambiguous.

**Please do not weaken or skip a check to make a PR pass.** If you believe a check is genuinely
miscalibrated, say so in the PR with the measurement that shows it, and change the check in a way
that still fails for the bug it was written to catch.

---

## Architecture rules

There is one rule, and it is not negotiable:

> **The simulation never reads input and never draws. The presentation never changes game state.**

Concretely:

1. **Tunable numbers go in a table, never inline.** Physics, grappling and match rules belong in
   [`src/Core/Cfg.cs`](src/Core/Cfg.cs); per-weapon values in
   [`src/Combat/Weapons.cs`](src/Combat/Weapons.cs); bot difficulty in `BotProfile.For` in
   [`src/Entities/Bot.cs`](src/Entities/Bot.cs).
2. **Game rules belong in the simulation.** Anything that decides what happens goes in
   `Simulation.cs` or `Actor.cs`, and must be reachable from `Simulation.Update(input, dt)` so the
   headless harness covers it automatically. A rule that only runs when a window is open is a rule
   that will not be tested.
3. **Visual and audio code only reads.** If you find yourself wanting to set state from
   `Renderer.cs`, `Hud.cs` or `Effects.cs`, the state probably belongs in the simulation or in
   `Effects.cs`.
4. **Keep the world axis-aligned.** Every brush is an AABB. This is what lets the navigation graph
   be derived from the geometry instead of authored by hand, so please do not introduce
   non-axis-aligned collision without discussing it first.
5. **New maps go through the catalog.** Add your layout to
   [`src/World/MapRegistry.cs`](src/World/MapRegistry.cs); `GameMap` will validate your spawns,
   derive bot pathing and place the cabinets automatically. Never hand-place a spawn that overlaps
   geometry — the sanitizer will move it, and the harness will notice if it cannot.
6. **No hand-committed binary assets.** All audio is synthesised at startup by `WavGen.cs`; please
   do not add sound files or loose textures. The weapon and character meshes are explicit
   exceptions and go through the checked-in build scripts in `tools/`. The supplied Industrial Zone
   GLB is the other exception: it is retained byte-for-byte with its required provenance and
   attribution record in [`assets/maps/`](assets/maps/).

[ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the reasoning behind these, including why the view
model is drawn into its own render target and how spawn sanitization keeps maps safe.

---

## Code style

- File-scoped namespaces; nullable reference types and implicit usings enabled.
- One type per file, named after the file.
- XML doc comments on public types and non-obvious members, explaining **why** — not restating the
  signature.
- Match the surrounding code. The existing style is plain, direct C#: no LINQ in the hot simulation
  loop, no allocations per tick where avoidable, no clever tricks without a comment explaining them.
- The build must stay at **0 warnings, 0 errors** (`dotnet build -warnaserror`).

---

## Reporting bugs

A useful bug report for this project includes:

- **What you did** — the exact command line, including `--seed` and `--map` if you used them.
- **What you expected** and **what happened**.
- **The headless report**, if the bug reproduces without a window. Because the simulation is
  deterministic given a seed, `dotnet run -- --headless --seconds 300 --seed <n>` often reproduces
  a gameplay bug exactly.
- **A captured PNG**, if it is visual: `dotnet run -- --screenshot shot.png --screen <name>`.

Balance feedback is welcome too, but please say which bot skill you played against and roughly how
long the match lasted. "The Sniper is too strong on Expert" is actionable; "the Sniper is too
strong" is not.

Remember the design contract: damage is intentionally flat, and the Double Barrel's alt-fire is
intentionally a movement tool. "Missing headshot multiplier" is a feature request, not a bug.

---

## Suggesting features

Check [docs/ROADMAP.md](docs/ROADMAP.md) first — key rebinding, an in-game settings screen and more
maps are already planned and known.

If your idea is not there, describe the **problem you want solved** rather than only the feature.
"Chasing a bhopping player across Playground is a coin flip because my hooks keep snapping to the
floor" is a better starting point than "add hook magnetism", and it may have a cheaper answer than
the one you had in mind.

---

## Pull request etiquette

- Keep pull requests focused. One fix or one feature per branch.
- Explain *why* in the description, not just what changed.
- Say explicitly which checks you ran and what they reported. If you could not run something — a
  GPU-dependent capture, for example — say so rather than leaving it ambiguous.
- Do not reformat or reorganise files unrelated to your change; it makes the diff unreviewable.
- Do not commit anything from `bin/`, `obj/`, or generated `shot.png`-style capture output.
  `.gitignore` covers these already — please do not remove those entries.

---

## Licence

This project is MIT licensed. By contributing you agree that your contributions are licensed under
the same terms.

HOODMATCH is an unofficial fan project inspired by Redmatch 2 (Etherous Games) with no connection
to their authors, and none to Valve Corporation. Please do not submit copyrighted material,
decompiled code, or data extracted from any commercial game.
