# ADR-0002: State model

**Status**: Accepted
**Date**: 2026-05-11

## Context

The Rust core ships these ADT shapes:
- `Mode` — closed payload-less sum.
- `Geometry` / `Brush` / `Action` — closed sums with mixed payloads.
- `Point<S>` / `ScreenRect<S>` — phantom-typed coordinates.
- `Opacity` / `Thickness` / `DimLevel` — validating newtypes (1..=255 / 1..=512 / 0..=255).
- `Result<T, E>` — closed Ok/Err.
- `reduce(&mut State, Action) -> StateDelta` — the state machine.

The C# port has to map each shape onto a C# 13 vocabulary that preserves the invariants without flattening them.

## Decision

| Rust | C# 13 |
|---|---|
| `enum Mode { Off, Bar, Mask, Vertical }` | `enum Mode : byte` + `static class ModeOps`. Switch expressions carry a defensive `_ => throw new UnreachableException()` arm — C# enums are open at the value level (`(Mode)42`), and that arm is the idiomatic cast-safety valve. |
| `enum Geometry { Rect(...) }` | `abstract record Geometry { sealed record Rect(...) : Geometry; }` with `private protected` ctor — closed to external derivation. |
| `enum Brush { Solid(...) }` | Same shape as `Geometry`. |
| `enum Action { CycleMode, …, BumpThickness(i16), … }` | Same shape — record DU for mixed-payload variants; payload-less variants expose `Instance` singletons. |
| `struct Logical; struct Physical;` | `interface ICoordSpace { static abstract string Name { get; } }` + two empty `readonly struct` impls. |
| `Point<S>` | `readonly record struct Point<TSpace>(int X, int Y) where TSpace : struct, ICoordSpace`. |
| `ScreenRect<S>` | Same pattern. |
| `Opacity(u8)` 1..=255 | `readonly record struct` with private ctor + `static Result<Opacity, CoreError> TryCreate(int)`. |
| `Result<T, E>` | Custom closed DU (`Ok` / `Err`) with `Match`/`Map`/`Bind`. No exceptions for expected validation failure. |
| `reduce(&mut, Action) -> StateDelta` | **Persistent** `(State Next, StateDelta Delta) Reduce.Apply(State, Action)`. C# records make `with`-update cheap; `ref State` is reserved for hot paths the state machine doesn't touch. |

## Why `Mode` is an enum and not a closed DU

Coverage gate temptation says "make Mode a record DU so switch is exhaustive without the `_ =>` arm". Rejected: payload-less variants are exactly what enum is for, and the `_ => throw` arm is the C# idiom for cast-coerced `(Mode)42`. Coverage is a quality indicator, not a target (memory: `feedback_coverage_is_indicator_not_target`).

## Why `Action`'s payload-less variants expose `Instance` singletons

`Reduce.Apply(state, Action.CycleMode.Instance)` reads cleanly and avoids per-call allocation. Record equality remains structural either way.

## Consequences

- `[ExcludeFromCodeCoverage]` is **not** allowed in domain code. The `_ => throw` arms ride below the coverage floor, which is where they belong.
- Phantom-typed coords are real type-system constraints — `Point<Logical>` and `Point<Physical>` cannot unify without an explicit converter.
- Validating newtypes funnel through `TryCreate` — no `Opacity` without bounds-checking.
