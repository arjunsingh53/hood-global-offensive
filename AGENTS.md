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
- **Pre-push gate:** `-warnaserror` build clean, both CI gates `RESULT: PASS`, `tools/parity`
  `PARITY: PASS`, `tools/presentation-check` build clean, and `tools/editmode-check` build clean.
  Push when the team asks or the work
  is production-ready, and update the push-policy bullet in HANDOFF.md with the new `origin/main`
  sync point.
- Build must stay clean: `dotnet build --nologo -warnaserror`
- Both CI gates must print `RESULT: PASS`:
  - `dotnet run -- --headless --seconds 300 --bots 9 --skill Normal --seed 20260114`
  - `dotnet run -- --headless --seconds 60 --bots 15 --skill Hard --seed 7331`
- The Unity sim port must stay in parity: build `tools/parity/Parity.csproj` and run it; it must
  end in `PARITY: PASS` (it also re-runs both CI gates through the ported runner, SHA-256 checks
  all 47 procedural audio cues regenerated through `unity/Assets/Scripts/Sim/Audio/` against
  `tools/parity/goldens/audio-hashes.txt`, and checks 21 reference-captured `AudioMix` routing
  goldens). Keep the port C# 9-compatible and never
  hand-edit `tools/parity/goldens/` — the audio manifest is regenerated from a reference
  `--sfx-dump` run plus SHA-256 hashing, nothing else.
- The Unity presentation layer has an editor-free compile gate: build
  `tools/presentation-check/PresentationCheck.csproj` clean with `-warnaserror`. It compiles
  `unity/Assets/Scripts/Presentation/**` (the `MatchDriver` scene bridge, the `SimAudioPlayer`
  audio bank, and the blockout pass: `MapBlockout`, `ActorViews`, `ViewModelSync`, `HudSync`)
  against `HGO.Sim` + `UnityStubs.cs`; extend the stub when new Unity APIs are used.
  Keep it C# 9-compatible. The edit-mode test assembly has the analogous
  gate: build `tools/editmode-check/EditModeCheck.csproj` clean with `-warnaserror`; it compiles
  `unity/Assets/Tests/EditMode/**` against `HGO.Sim` + `UnityTestStubs.cs` (stub NUnit +
  `Application.dataPath`).
- Screenshot capture needs a real GPU and is deterministic per map/seed (`--screenshot`,
  `--weapon`, `--zoom`, `--anim`).
- `--sfx-dump <dir>` writes every synthesised sound cue to WAV (headless machines have no audio
  device).
- Never commit anything under `.assets-scratch/` (local scratch) or `release/` (build output);
  README screenshots go in `shots/` with `git add -f` (that path is gitignored by design).
