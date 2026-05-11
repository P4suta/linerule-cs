# ADR-0007: Release pipeline

**Status**: Accepted (amended 2026-05-11 — two-frontend split)
**Date**: 2026-05-11

## Context

The Rust side uses `cargo-dist`. The C# side needs an analogue: tag → build → bundle → upload to GitHub Releases.

Additionally, the original v0.1 plan assumed a single `linerule.exe` for both
terminal use and end-user double-click. In practice that conflates two PE
subsystems: `Exe` (CUI) opens a console window on launch — fine in a
terminal, jarring on a double-click — and `WinExe` (GUI) does not, but then
silently discards stdout (System.CommandLine help, `config show` output, …).
The two needs are orthogonal, and the right answer is two frontends, not a
compromise binary.

## Decision

v0.1 ships **two front-end binaries** that share a common domain hexagon
(`Linerule.Core` / `Linerule.Config` / `Linerule.Platform.Windows` /
`Linerule.Diagnostics.Storage`):

| Project | OutputType | Filename | Use case |
|---|---|---|---|
| `src/Linerule.Cli` | `Exe` | `linerule.exe` | Terminal: `linerule run`, `linerule config …`, plus argless = run (default action on RootCommand) |
| `src/Linerule.App` | `WinExe` | `Linerule.exe` | Double-click: GUI launcher, no console window, calls `WindowsApp.RunAsync` directly |

Both are self-contained `dotnet publish` for `win-x64` (and `win-arm64`
opportunistically). Multi-dll layout — `PublishSingleFile=true` errors under
WindowsAppSDK 2.x without `EnableMsixTooling`. Native AOT remains aspirational
(ADR-0010).

The release workflow is triggered on tag `v*.*.*`. Steps:

1. `just publish` → `dotnet publish src/Linerule.Cli -c Release -r win-x64 /p:SelfContained=true -o artifacts/publish`.
2. `just publish-dist` → `dotnet publish src/Linerule.App -c Release -r win-x64 /p:SelfContained=true -o artifacts/publish-dist`.
3. (When AOT is green) `dotnet publish … /p:PublishAot=true` for the CLI.
4. Upload both directories (zipped) to the matching GitHub Release.

GitHub Actions are SHA-pinned (memory: `feedback_prefer_latest_not_pinning`); Dependabot bumps weekly.

## Consequences

- Two frontends to publish, but each adapter stays minimal (App's Program.cs
  is ~15 lines; Cli's bootstrap is now ~5 lines via shared `AppBootstrap`).
- The default action on `RootCommand` (argless = run) is a one-line change in
  `CliBuilder` that aligns terminal and GUI behavior — same code path
  (`RunCommand.Execute`) services `linerule`, `linerule run`, and the GUI
  launcher.
- No MSIX in v0.1 (deferred).
- Auto-update is deferred — both binaries are downloaded manually from Releases.
- Code-signing is deferred; SmartScreen will warn on first run for unsigned
  artifacts.
- FOLLOWUP: only the `Linerule.Cli` publish step is wired up so far (Justfile
  `publish` recipe + `ci.yml` "Publish (self-contained, win-x64)"). The
  `Linerule.App` half of the table above still needs a parallel `publish-dist`
  recipe and matching CI step; that work is tracked separately so this ADR can
  land alongside the slim-narrative pass.
