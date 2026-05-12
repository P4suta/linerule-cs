# ADR-0011b: Config integrity is two layers, not one

**Status**: Superseded by [ADR-0015](0015-tunables-as-compile-time-constants.md)
**Date**: 2026-05-11 (superseded 2026-05-12)

> Reversed 2026-05-12: with the config file removed (ADR-0015), there is nothing to validate
> at boot — `UserConfig.Default` is constructed from typed C# literals that the compiler
> already verifies. The two-layer integrity pipeline (FileIntegrity → Tomlyn → Validator
> → diagnostic-rich error rendering) was deleted along with `Linerule.Config`.

## Context

Users hand-edit `config.toml`. They will (and do) save typos, out-of-range values,
contradictory combinations, files with the wrong encoding, and sometimes literally
unrelated content. The original loader did shallow type checking and silently ignored
unknown keys, so a misspelled `base_opacty = 0.5` left the user staring at an HUD that
did not move — no error, no hint, no log line.

Two failure modes wanted to be expressible:

1. **File-layer**: the bytes on disk are not a sensible config artifact. Oversized, bad
   UTF-8, or — when the user opts into signing — tampered with since they last reviewed
   it.
2. **Schema-layer**: the bytes parse as TOML, the shape matches, but the *meaning* is
   wrong. A field is out of range; two fields contradict each other; a key is misspelled.

Folding them into a single error type loses the structural distinction (you read a sound
TOML file vs you read garbage) and forces every diagnostic to be either fatal or silent.

## Decision

The loader is a **5-stage pipeline behind `IConfigSource`**:

```
File bytes
  │  FileIntegrity.Read         — size cap, UTF-8 strict, optional SHA-256 sidecar
  ▼
TOML text
  │  Tomlyn TomlSerializer       — DOM parse, line/column spans on syntax errors
  ▼
TomlTable DOM
  │  RawConfigDeserializer       — DOM → RawUserConfig (nullable, schema-loose)
  │                                 collects unknown keys per section
  ▼
RawUserConfig (untrusted)
  │  Validator                   — range checks, smart-ctor invariants,
  │                                 cross-field invariants, unknown-key warnings
  ▼
typed UserConfig
```

Each stage owns a single responsibility:

- **FileIntegrity** is concerned with bytes, not meaning. Size cap (1 MB), UTF-8 strict
  decode (rejects invalid sequences instead of swallowing them as U+FFFD), and an
  optional `.sha256` sidecar that gives the user opt-in tamper detection (created with
  `linerule config sign`, checked on load, mismatches surface as **Warning** — not fatal
  — so a config edit doesn't lock the user out).
- **RawConfigDeserializer** does shape transcription. Every leaf field is nullable so the
  validator can distinguish "user didn't specify" from "user wrote 0". Per-section
  `UnknownKeys` lists are computed against a known-keys allowlist; unknown keys turn into
  Warning diagnostics in the validator (great for typo detection: `base_opacty` becomes
  "unknown key `hud.base_opacty` — Check for typos. Did you mean one of: ...?").
- **Validator** consumes the Raw DTO and produces either a typed `UserConfig` or an
  aggregated `ConfigError.SchemaDiagnostics`. Validation has three flavors: per-field
  range, smart-constructor (the Core newtypes `Thickness` / `Opacity` / `Rgba`), and
  cross-field invariants (HUD geometry vs padding, repeat ms relationships, foreground vs
  background identity + WCAG contrast, hotkey chord uniqueness, render warn ratio sanity).
  Every diagnostic carries Severity (Error / Warning / Info / Hint), DotPath
  (`hud.padding.edge`), Suggestion (free-text fix hint), and Related (other dot-paths
  involved in the same invariant).

Diagnostic richness is non-negotiable. The user is the operator; they should be able to
read a failure report and fix the file without consulting documentation. `DiagnosticPrinter`
renders Severity color, DotPath in backticks, suggestion under the primary message, and
related paths as a third line — Spectre.Console handles the color coding.

The pipeline sits behind a small `IConfigSource` interface so a future PR (PR 3) can swap
the in-process C# implementation for an out-of-process Rust validator binary
(`linerule-config-validate`) without touching call sites. `InProcessConfigSource` is the
always-available baseline.

## Consequences

- A misspelled key becomes a user-readable Warning with a typo-detection hint, instead of
  silent ignore.
- An out-of-range value becomes an Error with a Suggestion line stating the legal range +
  the documented default.
- A cross-field contradiction (HUD padding > HUD width / 2; initial-delay shorter than
  long-press threshold; foreground == background) becomes an Error with Related dot-paths
  so the user sees both sides of the constraint at once.
- Tamper detection is opt-in (`linerule config sign`), so a casual edit doesn't trip a
  Warning, but a deployer who signs their config gets a heads-up if the file changes.
- The Rust-validator swap (PR 3) is a single interface implementation away; no call site
  in the overlay binary needs to know which validator ran.
- Validation is **non-trivial code** that needs tests. Each cross-field invariant gets at
  least one targeted test; FsCheck property tests cover Print → Parse round-trip for the
  bounded input domain.
