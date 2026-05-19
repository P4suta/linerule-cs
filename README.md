# linerule-cs

Reading-ruler overlay for Windows. C# / .NET 10 + Windows App SDK 2.0 + Windows.UI.Composition (via the PowerToys `ICompositorDesktopInterop` pattern). The C# dual of [`linerule`](../linerule/) (Rust / winit / vello / wgpu / peniko).

## Why this exists

`Windows.UI.Composition.Compositor` + `ICompositorDesktopInterop` sits directly on DirectComposition, giving true per-pixel alpha on Day 1 — `Rgba.DefaultMask` is pure black `(0,0,0,0xCC)`. See [ADR-0009](docs/adr/0009-transparency-via-dcomp.md) for the full rationale.

## Layout

```
src/
  Linerule.Core/       net10.0           ADTs, render(), reduce() — pure
  Linerule.Config/     net10.0           Tomlyn schema + diagnostics
  Linerule.Platform/   net10.0-windows…  Composition + Win32 click-through + hotkeys
  Linerule.Cli/        net10.0-windows…  Exe (CUI) — `linerule.exe`, full subcommand surface
  Linerule.App/        net10.0-windows…  WinExe (GUI) — `Linerule.exe`, double-click launcher
  Linerule.XTask/      net10.0           defensive lint gate
tests/                                   xUnit v3 + FsCheck v3 + Verify
docs/adr/                                ADRs (mirror Rust 1:1)
```

Two frontends share the same `Linerule.Core` / `Linerule.Platform.Windows` / `Linerule.Diagnostics.Storage` hexagon — terminal use goes through the CLI, double-click goes through the GUI launcher. See ADR-0007 for the split.

## Download

Pre-built **rolling `latest`** AOT GUI build for Windows x64 (~8 MB, refreshed automatically on every `main` merge — see [ADR-0017](docs/adr/0017-rolling-release-pipeline.md)):

- [`Linerule-aot-win-x64.zip`](https://github.com/P4suta/linerule-cs/releases/latest/download/Linerule-aot-win-x64.zip) — the GUI launcher `Linerule.exe` (capital L). Unzip anywhere and double-click.
- [`sha256sums.txt`](https://github.com/P4suta/linerule-cs/releases/latest/download/sha256sums.txt) — SHA-256 of the zip above.

Binaries are not code-signed, so the first launch trips Windows SmartScreen. Choose **More info → Run anyway**. The release page itself lives at [github.com/P4suta/linerule-cs/releases/tag/latest](https://github.com/P4suta/linerule-cs/releases/tag/latest); the auto-generated body lists the conventional-commit subjects merged since the previous rolling cut.

For terminal use (`linerule.exe` CLI), build from source: `just publish` (see below).

## Build (Docker-first)

The dev container holds .NET 10 SDK + every Linux-side tool — host needs only Docker.

```sh
just docker-build  # one-time: build the dev image
just build         # Release build of the whole solution
just test          # All tests + branch-coverage collection
just coverage      # Branch-coverage report (advisory floor — see ADR-0008)
just lint          # CSharpier + dotnet format + typos + actionlint + xtask strict-code
just shell         # interactive bash inside the container
just ci            # Local replica of the GHA pipeline
```

The cross-platform projects (`net10.0`) build and test fully in the container. The Windows-targeted projects (`net10.0-windows10.0.22621.0`) also **build** in the container — WinAppSDK reference assemblies are platform-neutral. The produced executables only **run** on Windows. See ADR-0005.

```sh
just publish        # CLI Release  → artifacts/publish/linerule.exe       (terminal use)
just publish-dist   # GUI Release  → artifacts/publish-dist/Linerule.exe  (double-click)
just publish-debug  # CLI Debug    → artifacts/publish-debug/linerule.exe (WinDbg / dotnet-dump)
```

The distribution build (`publish-dist`) opens no console window on launch — copy the whole `artifacts/publish-dist/` directory to the target machine, then double-click `Linerule.exe`. Logs flow into `%APPDATA%\linerule\events.sqlite` (ADR-0012).

## Tooling (Day 1)

All baked into the dev container; no host-side install required.

- **CSharpier** — opinionated C# formatter, restored as a local dotnet tool from `.config/dotnet-tools.json`.
- **dotnet format** — Roslyn-driven formatter that complements CSharpier (CSharpier owns layout, dotnet-format owns analyzer-driven fixes).
- **typos** — repo-wide spell check. Allowlist in `_typos.toml`.
- **actionlint** — GitHub Actions linter for `.github/workflows/*`.
- **commitlint** — Conventional Commits enforcement. Local hook via lefthook; CI via `wagoid/commitlint-github-action`. Config: `commitlint.config.js`.
- **lefthook** — git hooks runner. `just hooks` installs the hooks; the binary itself runs in the container.
- **Linerule.XTask `strict-code`** — repo-specific banned-regex sweep.

```sh
# First-time setup on a fresh clone
just docker-build              # build dev image
just hooks                     # install lefthook git hooks
```

## CI

Linux + Windows parallel jobs on every PR / `main` push — see ADR-0008 for the job topology. Every action is SHA-pinned; Dependabot bumps weekly. CodeQL runs on Windows; Dependabot patch/minor PRs auto-merge.

## ADR pointers

- `docs/adr/0001-tech-stack.md` — WinAppSDK 2.0 / CsWin32 / Tomlyn / xUnit v3 rationale
- `docs/adr/0005-build-environment-exception.md` — Docker-first build; Windows is a runtime exception
- `docs/adr/0007-release-pipeline.md` — CLI/GUI two-frontend split
- `docs/adr/0008-ci-strategy.md` — CI job topology + SHA-pin policy
- `docs/adr/0009-transparency-via-dcomp.md` — per-pixel alpha via DComp
- `docs/adr/0012-sqlite-writer-only-event-store.md` — `events.sqlite` is the contract; analysis is external

## Analyzing the event log

The overlay writes structured events to a single SQLite database at
`%APPDATA%\linerule\events.sqlite` (WAL mode, multi-reader-safe — you can
query while the overlay is running). The binary itself ships no analysis
CLI; the file is the API. See ADR-0012 for the rationale.

**DuckDB (recommended for ad-hoc queries).** DuckDB reads SQLite
natively, gives you proper SQL + columnar speed, and is the canonical
"open this file and start asking questions" tool:

```sh
duckdb -c "
  INSTALL sqlite; LOAD sqlite;
  ATTACH '$APPDATA/linerule/events.sqlite' AS db (TYPE sqlite);
  SELECT subsystem, level, COUNT(*) AS n
  FROM db.events
  WHERE run_id = (SELECT run_id FROM db.runs ORDER BY started_at_utc DESC LIMIT 1)
  GROUP BY subsystem, level
  ORDER BY n DESC;
"
```

**sqlite3 (one-liner, no install if you have the OS package).**

```sh
sqlite3 "$APPDATA/linerule/events.sqlite" \
  "SELECT ts, subsystem, step FROM events ORDER BY ts DESC LIMIT 20;"
```

**Or just open it.** The file is plain SQLite — DBeaver, TablePlus,
DataGrip, your IDE's SQLite preview, and Python's stdlib `sqlite3` all
open it without setup. Find runs that crashed mid-flight with
`SELECT run_id, started_at_utc FROM runs WHERE ended_at_utc IS NULL;`.
