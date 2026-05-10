# ADR-0009: Transparency + click-through via WS_EX_LAYERED + Microsoft.UI.Composition

**Status**: Accepted (v2 — supersedes v1's NOREDIRECTIONBITMAP path)
**Date**: 2026-05-11

## v1 (deprecated) — what we tried first

- HWND created with `WS_EX_NOREDIRECTIONBITMAP` so DComp draws straight to DWM with no redirection surface.
- `Windows.UI.Composition.Compositor` + `ContentIsland.CreateForSystemVisual`.
- `bridge.ProcessesPointerInput = false` + `WS_EX_TRANSPARENT` + `WM_NCHITTEST → HTTRANSPARENT` for click-through.

**Verified empirically broken (2026-05-11).** Diagnostic logging proved:
- Our WndProc IS reached (`WM_NCHITTEST` count climbs ~55/sec).
- We DO return `HTTRANSPARENT`.
- Click `WM_LBUTTONDOWN` never reaches our WndProc.
- BUT clicks also never reach the underlying app (Notepad / Edge / Explorer).

The `WS_EX_NOREDIRECTIONBITMAP` window has no redirection surface for DWM to use as the cross-process hit-test target; DWM has nothing to route the click through. The clicks land in nothing.

We confirmed by swapping to `WS_EX_LAYERED + LWA_ALPHA` (uniform-grey test panel) — clicks immediately worked. That isolated the redirection-bitmap presence as the load-bearing constraint.

## v2 (accepted) — modern-only path

```
Window:
  WS_EX_LAYERED         — DWM uses the redirection bitmap as the composition target
  WS_EX_TRANSPARENT     — DWM routes WM_NCHITTEST + clicks through to underlying app
  WS_EX_NOACTIVATE      — never grabs focus
  WS_EX_TOOLWINDOW      — no taskbar / Alt-Tab presence
  WS_EX_TOPMOST         — always on top
  (NO WS_EX_NOREDIRECTIONBITMAP)
  (NO SetLayeredWindowAttributes — those are LWA_COLORKEY / LWA_ALPHA, the legacy GDI pathway)
  (NO UpdateLayeredWindow         — same legacy pathway)

Compositor: Microsoft.UI.Composition.Compositor
ContentIsland: ContentIsland.Create(Microsoft.UI.Composition.Visual)   ← modern API
Bridge:        DesktopAttachedSiteBridge.CreateFromWindowId(...)
                .ProcessesPointerInput = false
                .Connect(island)

DWM:
  - reads redirection bitmap from Composition output → composites per-pixel alpha to desktop
  - WS_EX_TRANSPARENT → routes mouse messages through to whatever's beneath
```

`Rgba.DefaultMask` is `(0, 0, 0, 0xCC)` — pure black at the per-pixel alpha we want. No "near-black to defeat the colour-key" lie embedded in the domain (the v1 ADR's primary motivation for choosing DComp over LWA_COLORKEY).

## Why this is data-driven

- Microsoft official docs (DirectComposition / Basic Concepts): "the composition target window can be a layered window; that is, it can have the WS_EX_LAYERED window style."
- Microsoft official docs (Modernize your desktop apps for Windows, 2026-04-27): "WinUI 3 is the modern native UI framework for Windows desktop apps and is the recommended path for new development."
- `ContentIsland.Create(Microsoft.UI.Composition.Visual)` is the modern API per WinAppSDK 2.0 reference (2026-05-02).
- Empirical click-through verification, 2026-05-11.

## What this also unlocks

- **Win2D integration**: Win2D 1.4's `CanvasComposition.CreateCompositionGraphicsDevice(Compositor, CanvasDevice)` projects under `<UseWinUI>true</UseWinUI>` as taking `Microsoft.UI.Composition.Compositor` — the same compositor the overlay uses. The HUD renders through Win2D into a `CompositionDrawingSurface` parented to the overlay's visual tree (no second window, no GDI fallback).
- **No `Windows.System.DispatcherQueue` plumbing**: required only by `Windows.UI.Composition.Compositor`. Removed — the Win32 entry point sets up `Microsoft.UI.Dispatching.DispatcherQueueController` and is done.

## Verification

- Unit test `RgbaTests.DefaultMask_is_pure_black_not_near_black` continues to assert the architectural invariant; under v2 it is structurally true (no LWA_COLORKEY to dodge).
- Runtime end-to-end: user runs the published exe; mask region renders with proper per-pixel alpha (slit shows desktop), clicks pass through to the underlying app, focus shifts. The HUD top-right shows status / hotkeys / telemetry rendered with DirectWrite text via Win2D.

## Consequences

**Unblocked**
- Win2D / DirectWrite / Direct2D in the overlay's visual tree, no second HWND.
- Mainstream Microsoft 2026 modernization story (WinAppSDK + Microsoft.UI.Composition + WinUI 3 ecosystem).

**Cost**
- The DWM redirection bitmap path is one extra GPU surface compared with v1's NOREDIRECTIONBITMAP. Negligible — we render a handful of small rects per frame, and the HUD is bounded to a 300×320 logical-px panel updated only on content change.

## Alternatives considered (and rejected)

- **Stay on `WS_EX_NOREDIRECTIONBITMAP` + DComp + add a second HWND for the HUD with GDI text**: works, but two windows + GDI is the "古き良き" retreat the user explicitly rejected.
- **Use raw Direct2D + DirectWrite via interop**: viable but adds substantial COM glue for marginal benefit — Win2D is the maintained .NET-friendly wrapper, and 1.4 is the current stable.
- **Migrate the overlay back to system Compositor (v1) and host HUD on a separate Microsoft.UI.Composition target**: two compositors in one process is doable but the architecture-tax outweighs the payoff.

## References

- Plan file: `~/.claude/plans/elegant-growing-planet.md`
- Empirical evidence (JSONL): user end-to-end runs 2026-05-11
- Microsoft docs (DirectComposition basic concepts, Modernize your desktop apps, ContentIsland 2.0 reference)
