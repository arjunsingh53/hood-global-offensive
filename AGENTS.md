# Agent notes

HOODMATCH is a C#/.NET 10 game built on Raylib-cs, with the simulation being ported to Unity
(`unity/`, engine-free `HGO.Sim` assembly). **Before making any change, read
[HANDOFF.md](HANDOFF.md)** — it carries the current project state, the verified
build/verify/release workflows, and the open tasks. Project conventions are documented in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/BUILDING.md](docs/BUILDING.md).

Quick reference:

- **After finishing every task, update [HANDOFF.md](HANDOFF.md)** — "Where the project stands",
  the code map, and the open items — so the next session (or a friend on another machine) can
  continue from a fresh `git pull` alone, and commit the update with the task.
- Build must stay clean: `dotnet build --nologo -warnaserror`
- Both CI gates must print `RESULT: PASS`:
  - `dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114`
  - `dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331`
- The Unity sim port must stay in parity: build `tools/parity/Parity.csproj` and run it; it must
  end in `PARITY: PASS` (it also re-runs both CI gates through the ported runner). Keep the port
  C# 9-compatible and never hand-edit `tools/parity/goldens/`.
- Screenshot capture needs a real GPU and is deterministic per map/seed (`--screenshot`,
  `--weapon`, `--zoom`, `--anim`).
- `--sfx-dump <dir>` writes every synthesised sound cue to WAV (headless machines have no audio
  device).
- Never commit anything under `.assets-scratch/` (local scratch) or `release/` (build output);
  README screenshots go in `shots/` with `git add -f` (that path is gitignored by design).
