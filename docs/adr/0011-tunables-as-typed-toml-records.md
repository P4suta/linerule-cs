# ADR-0011: Tunables as typed TOML records (config externalization)

**Status**: Accepted
**Date**: 2026-05-11

## Context

`linerule.exe` carried roughly 30 hard-coded `const float` / `const int` values across
`Linerule.Platform.Windows/` — HUD opacity, fade decay, colors, fonts, geometry,
hotkey-repeat timings, ruler defaults, render-budget warn ratio, telemetry refresh
cadence. Each "let me try a different value" required a recompile + republish, which
turned the iteration loop into a recompile cycle even when the underlying question was
purely "is 0.875 more readable than 0.6 for HUD opacity?". Worse, the constants were
scattered across half a dozen files and discovered only when someone went hunting for
them.

`Linerule.Config` already shipped a TOML loader (ADR-0001: Tomlyn + TOML, round-tripping
the Rust-side schema), but it covered only `[overlay]` and `[hotkeys]` — every behavioral
tunable inside the C# overlay binary lived as a `const`.

## Decision

Externalize every behavioral tunable as a typed record under `Linerule.Config`. The
new `UserConfig` is a 5-way product:

```
UserConfig = OverlayConfig × HotkeyMap × InputConfig × HudConfig × RenderConfig
```

with each section split into its own `sealed record` carrying a static `Default`. Core
domain newtypes (`Thickness`, `Opacity`, `Rgba`) stay in `Linerule.Core` — they encode
invariants the type system can enforce, and the validator funnels through their smart
constructors so the same invariants apply whether the value comes from code or from
TOML.

Three classes of value:

- **Behavioral tunable** → moves to config. HUD opacity (0.875), fade decay (120 px),
  repeat timings (250 / 250 / 400 / 50 ms), tap step (8 / 8), HUD geometry / padding /
  fonts / colors, render warn ratio (0.8), display-probe fallback (60 Hz). Every value
  is reachable from one TOML key via the documented schema.
- **Structural constant** → stays as `const`. `HudVisual.MinDrawIntervalMs = 33` (GPU
  resource floor — user-tuning produces crashes), `CullThreshold = 0.01f` (numerical
  zero), `HotkeyRepeater.ComputeNextStep` accelerating curve (algorithm shape, not a
  preference), `RenderBudget.OverrunSamplingPeriod = 60` (log-rate detail). Each gets
  a single-line `// NOT configurable — see ADR-0011` comment so the next reader knows
  why it didn't move.
- **Mathematically derived** → stays in code, never in config. premultiplied-alpha
  conversions, the chosen feature-level chain for D3D, etc.

Consumer wiring is constructor injection, not a singleton. The construction graph in
`WindowsApp.RunCoreAsync` is the single place that reads the resolved `UserConfig` and
threads its slices into `CreateHud(config.Hud)`, `new HotkeyRepeater(..., config.Input.Repeat)`,
`BuildBindings(map, config.Input.TapStep)`, `new TickLoop(..., config.Hud, config.Render.WarnRatio)`,
`RenderTiming.Probe(log, config.Render.FallbackRefreshHz)`. No service-locator, no
`IOptions<T>`, no global mutable state.

## Consequences

- A "try a different HUD opacity" cycle is one TOML edit + relaunch, no rebuild.
- The TOML schema is the single source of truth for "what a user can configure"; a
  contributor who adds a new tunable adds the schema key + the typed field + a
  default — and a missing default is a compile error.
- The split between "structural constant" vs "preference" is now an explicit, documented
  decision (`// NOT configurable — see ADR-0011`), not an accident of where someone
  happened to write a literal.
- Integrity is the validator's job; see ADR-0011b for the layered pipeline.
- Wiring fans out across many call sites (HUD / HotkeyRepeater / RenderClock / etc.) —
  this is the cost of constructor injection over a global, and is paid once at
  refactor time, not per-feature.
