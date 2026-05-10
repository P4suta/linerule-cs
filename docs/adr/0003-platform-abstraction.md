# ADR-0003: Platform abstraction surface

**Status**: Accepted
**Date**: 2026-05-11

## Context

The platform layer needs three concerns: an overlay surface that consumes `OverlayFrame`, a global hotkey host that produces `Action`s, and a cursor poller that produces `Point<Logical>`. The Rust workspace splits this into `linerule-platform` with mock + Windows impls.

## Decision

`Linerule.Platform` (TFM `net10.0`) owns:
- `IOverlaySurface` — `Apply(OverlayFrame)` + `MonitorBounds`.
- `IHotkeyHost` — `Register(ChordSpec, Action) -> Result<Unit, HotkeyError>` + `IAsyncEnumerable<Action> Subscribe(ct)`.
- `IMouseTracker` — `Point<Logical>? Poll()`.
- `ChordParser` — cross-platform, parses `"Ctrl+Alt+R"` → `ChordSpec`.
- `Mock/*` — in-memory test doubles for each interface.

`Linerule.Platform.Windows` (TFM `net10.0-windows10.0.22621.0`) owns the Windows impls:
- `OverlayWindow : IOverlaySurface` — `CreateWindowExW` + `Compositor.CreateDesktopWindowTarget`.
- `HotkeyHost : IHotkeyHost` — message-only window + `RegisterHotKey` + `Channel<Action>`.
- `CursorTracker : IMouseTracker` — `GetCursorPos`.

## Why split into two projects

Multi-targeting one project (`<TargetFrameworks>net10.0;net10.0-windows...</TargetFrameworks>`) was considered. Two projects is clearer: `Linerule.Platform` runs in a Linux Docker container (cross-platform parity with the Rust core); `Linerule.Platform.Windows` only builds on Windows. The slight cost is one extra `.csproj`.

## Why expose both `Subscribe` (async) and `TryDequeue` (sync) on the Windows host

The cross-platform contract is `IAsyncEnumerable`. The Windows main loop runs `PeekMessage` synchronously and drains the channel after each dispatch — `TryDequeue` is the right shape there. The async surface stays for mocks and any future cross-platform consumer.
