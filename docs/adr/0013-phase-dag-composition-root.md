# ADR-0013: Composition root is a typed Phase DAG, not an ordered void function

**Status**: Accepted
**Date**: 2026-05-11

## Context

`AppBootstrap.Install(args)` was the v0.1 composition root: a single static method
that opened the SQLite event sink, ran `Logger.Initialize`, then installed the
crash-dump emitter, in that exact order. The order was load-bearing — `Logger.For`
threw if `Initialize` hadn't run, and `CrashDump.Install` had to be wired before any
native code path could AV — but the only thing enforcing it was a paragraph of
comment text. Shuffle two lines in `Install` and the bug surfaces at runtime, in a
process that may not be able to log the failure because the logger isn't up yet.

Both entry points (`Linerule.Cli.Program` and `Linerule.App.Program`) called
`AppBootstrap.Install(args)` and then diverged: one routed into `CliBuilder`, the
other read the config separately and called `WindowsApp.RunAsync` directly. The
config-loading path was duplicated; the GUI entry silently fell back to defaults on
read errors while the CLI entry rendered diagnostics. Two places drifting.

We wanted the same shape to drive both entry points, with the order enforced
*structurally* — so a future contributor cannot accidentally re-shuffle the steps
into a configuration that fails in production but compiles fine locally.

## Decision

The composition root is a **typed Phase DAG** — Kleisli arrows in the
`Result<_, BootstrapError>` error monad, composed by `.Then`. Each phase emits a
capability-token record that the next phase consumes as its input type. C#
type-checking enforces ordering: `InitLogger.Phase()` has type
`Phase<SqliteSeed, LoggerSeed>`, so it cannot precede `OpenSqlite.Phase()` in a
composition — the wrong order is a CS1503 compile error.

The canonical sequence lives once in `Linerule.Diagnostics.Storage.BootDag.Default`:

```csharp
Phase<BootArgs, AppContext> BootDag.Default(string? configPath = null) =>
    OpenSqlite()        // BootArgs           ↝ SqliteSeed
        .Then(InitLogger())   // SqliteSeed   ↝ LoggerSeed
        .Then(InstallCrash()) // LoggerSeed   ↝ CrashSeed
        .Then(LoadConfig(configPath)) // CrashSeed ↝ ConfigSeed
        .Then(AssembleContext());     // ConfigSeed ↝ AppContext
```

Both entry points collapse to:

```csharp
var boot = await BootDag.Default().Run(BootArgs.FromArgv(args), default);
if (boot is Result<AppContext, BootstrapError>.Err err) return /* boundary fold */;
await using var ctx = ((Result<AppContext, BootstrapError>.Ok)boot).Value;
return await /* event loop with ctx */;
```

Failures lift through `LineruleError.FromBootstrap` so the CLI's
`DiagnosticPrinter` and the WinExe's silent-log path both fold the same
coproduct.

## Consequences

**Wins**

- **Order is a compile-time invariant**, not a comment. Adding a phase that
  requires `LoggerLive` (or one that produces a new token) propagates through the
  type system; misordered composition fails to build.
- **One source of truth.** `BootDag.Default` is the only place that knows the
  startup sequence; both entry points are 2-3 lines that just `Run(args)` and
  fold the result.
- **Tests can substitute phases** (`Phase.FromResult` / `Phase.Pure` factories)
  to construct an `AppContext` with an in-memory sink + null crash guard, without
  touching the production phase implementations.
- **Kleisli + monoidal laws verified** by `PhaseCompositionTests`: associativity,
  left/right identity, short-circuit on Err, exception trapping into
  `BootstrapError.Threw`, cancellation into `BootstrapError.Cancelled`.

**Trade-offs**

- One small new project (`Linerule.Bootstrap`) for the generic `Phase<TIn, TOut>`
  and `BootstrapError`. Concrete phases live in `Linerule.Diagnostics.Storage`
  where the resources they manage already live.
- The capability tokens (`SqliteSeed`, `LoggerSeed`, `CrashSeed`, `ConfigSeed`)
  are intentionally thin — they carry just enough to satisfy the next phase's
  input contract and the final `AppContext` assembly. They are *not* a place to
  hang arbitrary boot state; if a future phase needs more, it gets its own
  typed token.

**Considered and rejected**

- *Free-monad / interpreter pattern* would give us dry-run printability and a
  Symbol-style phase description, but for 5 phases it's overkill. Sealed-record
  arrows + `.Then` give the same compile-time DAG with 1/10 the machinery.
- *Applicative composition* (`Phase` running independent branches in parallel)
  is rejected because boot is inherently sequential — logger needs sink, crash
  guard needs logger. An `And` combinator is available for the rare independent
  pair but `Default` doesn't use it.
- *Monad transformer (`ResultT<M, T, E>`)* to thread the error type through every
  call site uniformly was tempting but C# can't express it without HKTs; the
  simulation is verbose and slow. `MapErr` / `Bind` at boundaries is six
  characters and obvious — keep it.

## Migration

The replaced `AppBootstrap.Install` is gone; the file is kept as a documentation
stub so `git grep AppBootstrap` lands the reader on the new entry. The
back-compat static `Logger.For` continues to work; `BootDag.InitLogger`
publishes the same `LoggerRoot` via `Logger.Initialize` so existing
`static readonly LoggerHandle Log = Logger.For(...)` sites pick it up.

## References

- `src/Linerule.Bootstrap/Phase.cs` — Kleisli combinators
- `src/Linerule.Diagnostics.Storage/BootDag.cs` — canonical phase sequence
- `src/Linerule.Diagnostics.Storage/AppContext.cs` — final boot output
- `tests/Linerule.Bootstrap.Tests/PhaseCompositionTests.cs` — algebraic-law tests
