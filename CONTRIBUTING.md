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

You need the .NET SDK 8.0 or newer (the project targets `net10.0`) and a GPU/display. Full details,
including the Python tooling used for visual verification, are in
[docs/BUILDING.md](docs/BUILDING.md).

---

## Before you open a pull request

Run the headless match. It exercises movement, collision, combat, damage, scoring and respawning in
one shot, and it exits non-zero if an invariant breaks:

```bash
dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114
```

You are looking for `RESULT: PASS`, and for the invariant block to report:

```
ticks with player embedded in geometry : 0
frames with non-finite transforms      : 0
kills recorded == deaths recorded      : yes
spawns are clear of geometry           : yes
```

If you changed anything visual — the renderer, the HUD, the menus — also regenerate and re-verify
the screenshot artifacts:

```bash
bash tools/capture_shots.sh
python tools/verify_shots.py
python tools/sweep_weapons.py
```

`tools/verify_shots.py` will fail on a weapon that drifts into the crosshair, on a scoped frame that
is not actually scoped, or on a title screen whose text has grown past the window edge. Those
failures are real; please fix the cause rather than loosening the threshold.

**Please do not weaken or skip a check to make a PR pass.** If you believe a check is genuinely
miscalibrated, say so in the PR with the measurement that shows it, and change the check in a way
that still fails for the bug it was written to catch.

---

## Architecture rules

There is one rule, and it is not negotiable:

> **The simulation never reads input and never draws. The presentation never changes game state.**

Concretely:

1. **Tunable numbers go in a table, never inline.** Physics and match rules belong in
   [`src/Core/Cfg.cs`](src/Core/Cfg.cs); per-weapon values including spray patterns in
   [`src/Combat/Weapons.cs`](src/Combat/Weapons.cs); bot difficulty in `BotProfile.For` in
   [`src/Entities/Bot.cs`](src/Entities/Bot.cs).
2. **Game rules belong in the simulation.** Anything that decides what happens goes in
   `Simulation.cs` or `Actor.cs`, and must be reachable from `Simulation.Update(input, dt)` so the
   headless harness covers it automatically. A rule that only runs when a window is open is a rule
   that will not be tested.
3. **Visual and audio code only reads.** If you find yourself wanting to set state from
   `Renderer.cs`, `Hud.cs` or `Effects.cs`, the state probably belongs in the simulation or in
   `Effects.cs`.
4. **Keep the world axis-aligned.** Every brush is an AABB. This is what lets the navigation graph be
   derived from the geometry instead of authored by hand, so please do not introduce non-axis-aligned
   collision without discussing it first.
5. **No hand-committed binary assets.** All audio is synthesised at startup by `WavGen.cs`; please do
   not add sound files or loose textures. Weapon meshes are the one exception, and they go through
   [`tools/build_weapon_asset.py`](tools/build_weapon_asset.py): the repository holds the optimised
   result, the script holds the recipe, and the source asset is never committed.

[ARCHITECTURE.md](docs/ARCHITECTURE.md) explains the reasoning behind these, including why the view
model is drawn into its own render target, how an art-mesh weapon is placed, and why the far clip
plane is set explicitly.

---

## Code style

- File-scoped namespaces; nullable reference types and implicit usings enabled.
- One type per file, named after the file.
- XML doc comments on public types and non-obvious members, explaining **why** — not restating the
  signature.
- Match the surrounding code. The existing style is plain, direct C#: no LINQ in the hot simulation
  loop, no allocations per tick where avoidable, no clever tricks without a comment explaining them.
- The build must stay at **0 warnings, 0 errors**.

---

## Reporting bugs

A useful bug report for this project includes:

- **What you did** — the exact command line, including `--seed` if you used one.
- **What you expected** and **what happened**.
- **The headless report**, if the bug reproduces without a window. Because the simulation is
  deterministic given a seed, `dotnet run -- --headless --seconds 300 --seed <n>` often reproduces a
  gameplay bug exactly.
- **A screenshot or the captured PNG**, if it is visual. `--screenshot` plus the measurement from
  `tools/inspect_png.py` is far more actionable than a description.

Balance feedback is welcome too, but please say which bot skill you played against and roughly how
long the match lasted. "The AWP is too strong on Expert" is actionable; "the AWP is too strong" is
not.

---

## Suggesting features

Check [docs/ROADMAP.md](docs/ROADMAP.md) first — armour penetration, armour on respawn, the stomach
hitbox and a config file are already planned and known.

If your idea is not there, describe the **problem you want solved** rather than only the feature.
"Holding the west doorway is a coin flip because I cannot see who is flanking" is a better starting
point than "add a minimap", and it may have a cheaper answer than the one you had in mind.

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

Hood Global Offensive is an unofficial fan project with no connection to Valve Corporation. Please
do not submit copyrighted material, decompiled code, or data extracted from any commercial game.
