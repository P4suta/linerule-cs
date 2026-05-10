# ADR-0010: Native AOT readiness — aspirational, not blocking

**Status**: Accepted
**Date**: 2026-05-11

## Context

Native AOT is the right destination for an overlay binary: instant startup, smaller footprint, no JIT. Reality on .NET 10 + WinAppSDK 2.0:

- WinAppSDK 2.0 has improving but uneven AOT support; XAML markup and CsWinRT projection regressions still surface.
- Tomlyn is reflection-based; AOT compatibility needs verification at integration time.
- `System.CommandLine` 2.x has source-generator support and is largely AOT-friendly.

Treating AOT as a v0.1 release blocker is unrealistic. Treating it as "we'll get to it" silently breaks the moment someone introduces reflection.

## Decision

- **Code is AOT-ready from Day 1.** No `dynamic`, no `Activator.CreateInstance(Type)`, no `System.Reflection.Emit`. `<IsAotCompatible>true</IsAotCompatible>` in `Directory.Build.props` enables the trim/AOT analyzers as warnings; we promote them to errors via `.editorconfig`. `Linerule.XTask strict-code` greps for the same patterns as a defensive lower bound.
- **`<PublishAot>` is aspirational.** `just publish-aot` exists; it may fail on WinAppSDK regressions. CI runs it as an opt-in canary, not a blocker.
- **JSON via STJ source generators only.** `[JsonSerializable(typeof(...))]` on a partial `JsonSerializerContext`.
- **TOML via Tomlyn.** Reflection acknowledged; if AOT integration shows trim warnings, replace with a hand-rolled schema mapper. The schema is small enough.

## Consequences

- A future `just publish-aot` becoming green is a release event, not a refactor — the code never has to be ported.
- A contributor who introduces reflection trips both the Roslyn AOT analyzer (build error) and the strict-code grep (CI error).
