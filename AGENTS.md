# Agent notes

HOODMATCH is a C#/.NET 10 game built on Raylib-cs. **Before making any change, read
[HANDOFF.md](HANDOFF.md)** — it carries the current project state, the verified
build/verify/release workflows, and the open tasks. Project conventions are documented in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/BUILDING.md](docs/BUILDING.md).

Quick reference:

- Build must stay clean: `dotnet build --nologo -warnaserror`
- Both CI gates must print `RESULT: PASS`:
  - `dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114`
  - `dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331`
- Screenshot capture needs a real GPU and is deterministic per map/seed (`--screenshot`,
  `--weapon`, `--zoom`, `--anim`).
- `--sfx-dump <dir>` writes every synthesised sound cue to WAV (headless machines have no audio
  device).
- Never commit anything under `.assets-scratch/` (local scratch) or `release/` (build output);
  README screenshots go in `shots/` with `git add -f` (that path is gitignored by design).
