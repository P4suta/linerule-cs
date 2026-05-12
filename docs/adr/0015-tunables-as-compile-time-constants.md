# ADR-0015: Tunables as compile-time constants

**Status**: Accepted
**Date**: 2026-05-12

## Context

ADR-0011 externalized ~30 tunables (HUD opacity, fade decay, colors, fonts, geometry,
hotkey-repeat timings, render budget) into a TOML config file at
`%APPDATA%\linerule\config.toml`, deserialized by Tomlyn. ADR-0011b layered structured
validation on top. The premise was: "rebuilding for a tweak is friction; users will tune
opacity, want different colors, rebind hotkeys."

That premise didn't survive contact with reality:

- **Zero real-world tuners.** The only consumer who edits the file is the maintainer,
  and the maintainer's iteration loop is "edit code → `just build-release` → reload",
  which is the same loop a rebuild would be.
- **Tomlyn is reflection-based** and **blocks `<PublishAot>`** for `Linerule.Cli` and
  `Linerule.App`. ADR-0010 (Phase 1) annotated the call chain with `[RequiresUnreferencedCode]`
  / `[RequiresDynamicCode]` and added composition-root `[UnconditionalSuppressMessage]`
  shims, which made the **analyzer** stop complaining but does **not** make the actual AOT
  trim step work — Tomlyn-reachable types get trimmed away at native publish, and the
  binary fails at runtime.
- **Schema is small enough.** 5 records, ~30 fields, all primitives or simple sub-records.
  Encoding them as `static readonly` literals is the same code volume as the TOML defaults
  in `UserConfig.Default`, just colocated with the consumers.
- **The diagnostic infrastructure** (FileIntegrity, Tomlyn, RawConfigDeserializer,
  Validator, RangeValidator, CrossFieldValidator, UnknownKeyValidator, SchemaDiagnostics,
  ConfigError union, SeverityLattice, …) costs ~10 source files and 53 tests to maintain.
  All of that exists to validate *what the user typed*. Without a user-typed file, there
  is nothing to validate.

## Decision

- **Retire `config.toml`** entirely. There is no on-disk config file, no `%APPDATA%\linerule\`
  directory entry, no `.sha256` sidecar, no `linerule config edit / show / path / print-default
  / validate / sign` subcommands.
- **`UserConfig` lives in `Linerule.Core`**. The 5-record product (`OverlayConfig`,
  `HotkeyMap`, `InputConfig`, `HudConfig`, `RenderConfig`) is preserved verbatim — every
  consumer still takes a `UserConfig` parameter, so dependency injection is unchanged.
- **`UserConfig.Default` is the single source of truth.** Every tunable is a `static readonly`
  literal on the appropriate sub-record's `.Default`.
- **`Linerule.Config` project is deleted.** Tomlyn dependency disappears. The 53
  `Linerule.Config.Tests` go with it (most asserted properties of the loader and validators
  that no longer exist).
- **The boot DAG drops the `LoadConfig` phase.** `BootDag.Default` is now
  `OpenSqlite ≫ InitLogger ≫ InstallCrash ≫ AssembleContext`, with `UserConfig.Default`
  injected directly. ADR-0010 Phase 1's `AotBoundary` shims (Cli + App + tests) become
  no-ops and are deleted; the IL2026/IL3050 chain they suppressed is gone because the
  Tomlyn-using methods are gone.
- **Future tunability** (if any consumer ever actually wants it) goes through env vars on
  the affected sub-record (e.g. `LINERULE_HUD_OPACITY=0.5`), parsed at boot, applied as
  a `with`-record clone over `UserConfig.Default`. Not implemented now; documented as the
  forward-compatible escape hatch when the need actually arises.

## Consequences

- `<PublishAot>` for `Linerule.Cli` and `Linerule.App` becomes mechanically achievable —
  the remaining blocker is WinAppSDK / CsWinRT projection (addressed in ADR-0010 Phase 2).
- Build/test surface contracts: ~10 source files + 53 tests deleted. The `UserConfig`
  record API is otherwise byte-identical, so consumer code reads and edits like before.
- "Edit a tunable" workflow: change the literal in `UserConfig.Default`,
  `just build-release`, reload. Same number of steps as "edit a const", because that's
  what it is.
- Lost capabilities: per-user theming via TOML, runtime hotkey rebinding via TOML,
  `linerule config` subcommand surface. None had real users.
- The `Tunables-as-typed-records` ADR (0011) and `Config integrity is two layers`
  (0011b) are both **Superseded** by this one.

## Relation to other ADRs

- **ADR-0010 (AOT readiness gap)**: this ADR removes the largest AOT blocker (Tomlyn).
  ADR-0010 Phase 2 follows up by removing the second blocker (WinAppSDK / CsWinRT).
- **ADR-0011 / 0011b**: superseded.
- **ADR-0001 (tech stack)**: TOML is no longer in the dependency set; Tomlyn removed
  from `Directory.Packages.props`.
