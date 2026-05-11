# ADR-0014: Config validation is applicative; severity is a join-semilattice

**Status**: Accepted
**Date**: 2026-05-11

## Context

The v0.1 `Validator` was a 772-line `static class` with one giant `Validate`
method. Each TOML section had its own hand-written per-field checker;
`CheckIntRange`, `CheckLongRange`, `CheckFloatRange`, and `CheckDoubleRange`
were four near-identical 32-line copies that differed only in element type.
Unknown-key reporting fanned out to nine ad-hoc `ReportUnknownKeys` calls at
the top of `Validate`. Smart-constructor failures from `Linerule.Core`
(`Opacity.TryCreate`, `Thickness.TryCreate`) collapsed via `ToHumanString()`
into the `Message` field of a `ConfigDiagnostic`, throwing away the typed
`CoreError` so tooling could only substring-match the rendered English.

`DiagnosticSeverity` was an enum with the values declared as
`Error=0, Warning=1, Info=2, Hint=3` — numerically inverted relative to the
semantic ordering everyone reads it as. Aggregation was a
`List<ConfigDiagnostic>` + `diags.Exists(d => d.IsError)`; concatenating two
section results was a `foreach` loop.

We wanted three things:

1. **All errors at once.** Monadic `Bind` short-circuits on the first failure,
   which is exactly the wrong UX for config validation — users want to see
   every problem in a single pass.
2. **One range-check implementation.** The four duplicates only differ in the
   element's element type; .NET 8's `INumber<T>` makes that parametrically
   polymorphic.
3. **Algebraic aggregation.** Severity has a natural ordering and a natural
   accumulator (the running maximum). That's a bounded join-semilattice; the
   bag of diagnostics is its monoid.

## Decision

### Severity as a bounded join-semilattice `(DiagnosticSeverity, ⊔, Hint)`

The enum is reordered to `Hint=0, Info=1, Warning=2, Error=3` so that
`Math.Max((int)a, (int)b)` *is* the join (⊔). `SeverityLattice` exposes
`Join` / `Bottom` / `Top`; `DiagnosticBag` (the monoid) maintains the running
join in `O(1)` per insertion. `Combine` is associative with `Empty` as
identity. Laws are property-tested by enumerating all 4×4×4 triples.

### Applicative `Validation<T>` instead of monadic `Result<T, _>`

```csharp
public readonly record struct Validation<T>(T Value, DiagnosticBag Errors);

public static Validation<C> Apply2<A, B, C>(
    Validation<A> a, Validation<B> b, Func<A, B, C> f);
// ... Apply3..Apply7
```

Both branches always run; their bags concatenate via the monoid; the combining
function is always invoked because every branch supplies a usable fallback in
`Value` (range-rejected fields fall back to the documented default). Failures
surface through `Errors`, not through skipping `f`. This is the textbook
shape that makes Haskell's `Validation` type *not* a monad — the very
property the config UX requires.

### Generic range check `RangeValidator.InRange<T : INumber<T>>`

One implementation; the four typed wrappers (`CheckIntRange` etc.) become
three-line delegating shims. NaN handling is uniform via `T.IsNaN` (no-op for
integer instantiations, IEEE-754 for `float` / `double`).

### Typed `Cause` on `ConfigDiagnostic`

`ConfigDiagnostic` gains an optional `CoreError? Cause` field. When a
smart-constructor rejects an out-of-range value, the diagnostic carries the
typed `CoreError.Opacity` / `CoreError.Thickness` instead of (only) the
rendered English. Tooling and tests can pattern-match instead of substring.

### Cross-field invariants live in `CrossFieldValidator`

The HUD-padding-vs-width / repeat-cadence / contrast-ratio / hotkey-uniqueness
checks move into their own file. They run on already-resolved section values,
not as another applicative branch — they're independent of the per-field
errors and have their own dot-paths.

## Consequences

`Validator.cs` collapses from **772 lines to 256**. The duplicated
`CheckIntRange`/`CheckLongRange`/`CheckFloatRange`/`CheckDoubleRange`
implementations are gone. Unknown-key reporting is per-section via
`UnknownKeyValidator.WithUnknownKeys` — composable as a `Validation<T>`
combinator, no more nine-call list at the top of `Validate`. Cross-field
checks are in `CrossFieldValidator` (198 lines), color contrast utilities in
`ColorContrast` (34 lines).

Existing tests stay green because:

- The enum names (`Error`, `Warning`, `Info`, `Hint`) are unchanged; only the
  numeric values reorder.
- `IsError` semantics are preserved (`Severity == Error`).
- `DiagnosticBag.IsFatal` collapses the legacy `diags.Exists(d => d.IsError)`
  into a one-line lattice predicate (`Severity >= Error`).
- `ConfigDiagnostic.Cause` is additive (nullable, defaults to null).

The applicative API is small enough that adding a new section is two lines:
each field becomes a `RangeValidator.InRange(...)` call, `Apply{N}` ties them
together, `WithUnknownKeys(...)` folds the section's unknown-keys list in.

## Considered and rejected

- **Monad transformer.** `ResultT<M, T, E>` over `IEnumerable<ConfigDiagnostic>`
  would give us a unified abstraction but C# lacks HKTs; the simulation is
  verbose and the boundary marshaling is no cleaner than what we have today.
- **LINQ comprehension `from x in ... select ...`.** Requires both `Select`
  and `SelectMany` (i.e. monadic); applicative-only validation rules that
  out. We use the explicit `ApplyN(...)` family, which is honest about the
  semantics.

## References

- `src/Linerule.Config/Validation.cs` — `Validation<T>` + `Apply2..Apply7`
- `src/Linerule.Config/RangeValidator.cs` — `INumber<T>` generic range check
- `src/Linerule.Config/SeverityLattice.cs` — bounded join-semilattice
- `src/Linerule.Config/DiagnosticBag.cs` — monoid `(Bag, Combine, Empty)`
- `src/Linerule.Config/UnknownKeyValidator.cs` — per-section combinator
- `src/Linerule.Config/CrossFieldValidator.cs` — cross-field invariants
- `tests/Linerule.Config.Tests/SeverityLatticeTests.cs` — exhaustive law tests
- `tests/Linerule.Config.Tests/DiagnosticBagTests.cs` — monoid law tests
- `tests/Linerule.Config.Tests/ValidationTests.cs` — applicative behavior
- `tests/Linerule.Config.Tests/RangeValidatorTests.cs` — boundary / NaN tests
