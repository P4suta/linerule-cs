# ADR-0009: Transparency + click-through via WS_EX_LAYERED + WS_EX_NOREDIRECTIONBITMAP + Windows.UI.Composition + ICompositorDesktopInterop

**Status**: Accepted (v3 — supersedes v2's WS_EX_LAYERED-only / Microsoft.UI.Composition path)
**Date**: 2026-05-11

## Accepted: canonical PowerToys pattern

```
HWND ex-style:
  WS_EX_LAYERED              — DWM routes WM_NCHITTEST + clicks through
                               to the window beneath when paired with
                               WS_EX_TRANSPARENT. Load-bearing for
                               cross-process click-through.
  WS_EX_TRANSPARENT          — click-through hint.
  WS_EX_NOREDIRECTIONBITMAP  — DComp draws directly to its own GPU surface;
                               DWM composites per-pixel alpha. No GDI
                               redirection bitmap. NOT mutually exclusive
                               with WS_EX_LAYERED — PowerToys ships them
                               together; LAYERED owns click routing,
                               NOREDIRBM owns the rendering path.
  WS_EX_NOACTIVATE           — never grabs focus.
  WS_EX_TOOLWINDOW           — no taskbar / Alt-Tab presence.
  WS_EX_TOPMOST              — always on top.
  (NO SetLayeredWindowAttributes — those are LWA_COLORKEY / LWA_ALPHA,
   the legacy GDI pathway. Composition writes the surface; DWM owns
   the windowing semantics.)
  (NO UpdateLayeredWindow    — same legacy pathway.)

Compositor:    Windows.UI.Composition.Compositor   (system, NOT Microsoft.UI.Composition)
Target:        ICompositorDesktopInterop.CreateDesktopWindowTarget(hwnd, isTopmost: false, out target)
                NO ContentIsland, NO DesktopAttachedSiteBridge.
Root visual:   ContainerVisual { RelativeSizeAdjustment = (1, 1) } → target.Root
```

`ICompositorDesktopInterop` GUID `29E691FA-4567-4DCA-B319-D0F207EB6807`; QueryInterface via `(ICompositorDesktopInterop)(object)compositor`.

`Rgba.DefaultMask` is `(0, 0, 0, 0xCC)` — pure black at the per-pixel alpha we want. No "near-black to defeat the color-key" lie embedded in the domain.

## Why this is data-driven

- **Production reference**: `microsoft/PowerToys` `src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp` ships exactly this use case.
- **Official C# binding**: `microsoft/Windows.UI.Composition-Win32-Samples` `dotnet/WPF/AcrylicEffect/CompositionHost.cs` is the Microsoft C# sample for `ICompositorDesktopInterop`.
- **Empirical bisection**: `WS_EX_NOREDIRECTIONBITMAP` alone drops cross-process clicks; `Microsoft.UI.Composition` + `DesktopAttachedSiteBridge` wedges natively on `WS_EX_LAYERED`. This combination is the remainder.
- **Microsoft official docs (DirectComposition / Basic Concepts)**: "the composition target window can be a layered window; that is, it can have the WS_EX_LAYERED window style."

## What this also unlocks

- **HUD rendering pipeline (Microsoft-pure)**: `Windows.UI.Composition.CompositionDrawingSurface` → `ICompositionDrawingSurfaceInterop.BeginDraw(IID_ID2D1DeviceContext)` → `ID2D1DeviceContext`, direct Direct2D + DirectWrite drawing into the same composition target as the overlay. Direct2D / DirectWrite / Direct3D11 / DXGI types are source-gen'd by `Microsoft.Windows.CsWin32` from `NativeMethods.txt`. Third-party D2D wrappers (Win2D, Vortice, SharpDX, …) are out — Microsoft official (CsWin32 + win32metadata + CsWinRT) is the entire interop story. The HUD shares the overlay's compositor: one HWND, one visual tree, no GDI fallback.
- **Two DispatcherQueues on the UI thread**: `new Windows.UI.Composition.Compositor()` requires a `Windows.System.DispatcherQueue` on the calling thread. `Microsoft.UI.Dispatching.DispatcherQueueController` (used by timers + event-loop exit) does NOT satisfy this — the WinAppSDK lifted queue and the system queue are independent. `WindowsApp.RunCoreAsync` stands up both: `Windows.System.DispatcherQueueController.CreateOnCurrentThread` first (load-bearing for the Compositor), then `Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread` for the timers. Both queues coexist on the same thread per WinAppSDK docs.
- **No `Microsoft.Graphics.Win2D` dependency**: Win2D hard-couples to `Microsoft.UI.Composition.Compositor` and refuses `Windows.UI.Composition.Compositor`. Dropping it removes the only third-party graphics package from the Platform.Windows assembly.

## Verification

- Unit test `RgbaTests.DefaultMask_is_pure_black_not_near_black` continues to assert the architectural invariant; under v3 it is structurally true (no LWA_COLORKEY to dodge).
- Runtime sanity JSONL: each end-to-end run must emit these five lines in order before the overlay is considered "live":
  ```
  Composition  | Windows.UI.Composition.Compositor created
  Composition  | DesktopWindowTarget bound to HWND
  Composition  | root visual attached
  OverlayWindow | ShowWindow ok — overlay live
  Hud          | HUD visual attached
  ```
- End-to-end: user runs the published exe; mask region renders with proper per-pixel alpha (slit shows desktop), clicks pass through to the underlying app (Notepad / Edge receive focus), `click reached overlay` warning stays at 0. The HUD top-right shows status / hotkeys / telemetry rendered with DirectWrite text via Win2D.

## Consequences

**Unblocked**
- Click-through on `WS_EX_LAYERED` actually works (DWM routes through to the underlying app).
- Per-pixel alpha through DComp (`WS_EX_NOREDIRECTIONBITMAP` keeps the redirection-surface step out).
- Win2D / DirectWrite / Direct2D in the overlay's visual tree, no second HWND.

**Cost**
- Adds one DWM composition step (LAYERED window is part of DWM's compose chain) over a "pure" NOREDIRBM-only path. Negligible — we render a handful of small rects per frame, and the HUD is bounded to a 300×320 logical-px panel updated only on content change.
- We deliberately step off the WinUI 3 / Microsoft.UI.Composition / ContentIsland modernization story. The PowerToys pattern is the production-tested route for this specific use case; Microsoft has not yet shipped a `Microsoft.UI.Composition`-equivalent interop for non-XAML hosts.

## Alternatives considered (and rejected)

- **Microsoft.UI.Composition + ContentIsland + DesktopAttachedSiteBridge**: wedges natively on `WS_EX_LAYERED` HWNDs — does not cover the click-through transparent overlay use case.
- **`WS_EX_NOREDIRECTIONBITMAP` alone + DComp**: drops cross-process clicks; DWM has no redirection surface to route through.
- **Microsoft.Graphics.Win2D**: hard-binds to `Microsoft.UI.Composition.Compositor` and rejects `Windows.UI.Composition.Compositor` (CS1503/CS0103). Type projection does not change with `UseWinUI=false`.
- **Vortice.Windows (Direct2D1 + Direct3D11)**: mainstream .NET binding but third-party. CsWin32 + win32metadata provides equivalent source-gen; no additional OSS dependency needed.

## References

- `microsoft/PowerToys` `src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp`
- `microsoft/Windows.UI.Composition-Win32-Samples` `dotnet/WPF/AcrylicEffect/CompositionHost.cs`
- Microsoft docs (DirectComposition basic concepts; `ICompositorDesktopInterop` API reference)
