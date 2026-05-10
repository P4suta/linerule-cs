# ADR-0006: Workspace lints — single source of truth

**Status**: Accepted
**Date**: 2026-05-11

## Context

Memory `feedback_workspace_lints_single_source_of_truth` and `feedback_no_warning_suppression` warn against per-project lint carve-outs and inline suppressions. The Rust side enforces this via `[workspace.lints]`. The C# equivalent is `Directory.Build.props` + `.editorconfig`.

## Decision

All analyzer / Roslyn rules live in **one** of two files:

1. `Directory.Build.props` — language version, warning level, AOT flags, the analyzer NuGet packages, the banned-symbols `AdditionalFiles` reference. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. `<NoWarn></NoWarn>` (empty — no global suppressions).
2. `.editorconfig` — per-rule severity overrides. The default is the analyzer suite's defaults; we escalate IDE0010 / IDE0072 / CA1062 / CA2007 / CA2016 / CA1851 to `error`.

Per-project `<NoWarn>` is forbidden. `[SuppressMessage]` is forbidden in domain code (banned by `Linerule.XTask` `strict-code`). The only allowed escape hatch — `[SuppressMessage(..., Justification = "<fixed-string>")]` for unavoidable cases — uses a fixed Justification string that the strict-code sweep recognizes.

## Consequences

- A new analyzer rule landing on green is a single-file edit in `.editorconfig` — same as the Rust `[workspace.lints.clippy]` block.
- A "warning fatigue" PR cannot merge — it has to fix the warning, not silence it (memory: `feedback_no_warning_suppression`).
- The XTask defensive sweep enforces the policy at the regex level for fast feedback.
