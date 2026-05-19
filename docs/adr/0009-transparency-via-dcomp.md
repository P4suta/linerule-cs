# ADR-0009: Transparency + click-through via WS_EX_LAYERED + WS_EX_NOREDIRECTIONBITMAP + DirectComposition

**Status**: Accepted (v4 — dcomp-direct, supersedes v3's `Windows.UI.Composition.Compositor` route)
**Date**: 2026-05-11 (amended 2026-05-12; amended 2026-05-19 — `DCompositionCreateDevice2` rendering device must be `ID2D1Device`, not `ID3D11Device`, for `IDCompositionSurface::BeginDraw(IID_ID2D1DeviceContext)` to be legal — bug caught by first hardware test of the v4 path)

> **2026-05-12 amendment (ADR-0010 Phase 2)**: the composition pipeline below
> the HWND ex-style stack is now **`dcomp.dll` direct** —
> `DCompositionCreateDevice2(d2dDevice, IID_IDCompositionDesktopDevice, out)`
> + `device.CreateTargetForHwnd` + `IDCompositionVisual2` /
> `IDCompositionSurface`, all source-gen'd through `Microsoft.Windows.CsWin32`.
> The `d2dDevice` is itself minted via
> `D3D11CreateDevice(BGRA_SUPPORT) → IDXGIDevice → ID2D1Factory1.CreateDevice`
> (see `D3D11Devices.cs` — both `CreateBgra` and `CreateD2DDevice` live there).
> **The choice of rendering device matters at runtime**: per the
> `IDCompositionSurface::BeginDraw` API contract, the IID requested via
> `BeginDraw(iid, …)` must match the type family of `renderingDevice` — a
> D3D11-rooted dcomp device can only return DXGI surfaces; only a D2D-rooted
> dcomp device can return `ID2D1DeviceContext` via
> `BeginDraw(IID_ID2D1DeviceContext, …)`. The original v4 draft passed
> `d3dDevice` directly and shipped that way; the resulting `E_NOINTERFACE`
> manifested as a per-frame `InvalidCastException` swallowed by
> `HudRenderer.Draw`'s try/catch (HUD silently never repainted). Phase 2's
> CI gate caught the build but not the runtime — first hardware run
> exposed it on 2026-05-19, and the rendering-device argument was flipped
> to `d2dDevice` then.
> The `Windows.UI.Composition.Compositor` route (v3) had a hard dependency
> on the WinAppSDK + CsWinRT projection layer, which prevented `<PublishAot>`
> from succeeding (`MarshalInspectable<T>.FromAbi(ptr)` is not fully
> source-genned in CsWinRT 2.x). Going direct to dcomp drops the WinRT
> projection requirement entirely; the same `WS_EX_LAYERED + WS_EX_NOREDIRECTIONBITMAP`
> ex-style mix from v3 carries over unchanged — only the API used to mint
> the visual tree changed. PowerToys MouseHighlighter remains the production
> reference; Microsoft itself uses `dcomp.dll` directly in C++ system code.
> Visuals don't have a direct `SetOpacity` in dcomp, so the HUD attaches
> an `IDCompositionEffectGroup` (which does) for cursor-fade. The vsync
> pacer thread calls `DwmFlush()` then `PostMessage(WM_APP_TICK)` to the UI
> thread; rendering / commit happen single-threaded on the UI thread. See
> ADR-0010 Phase 2 for the full migration rationale.

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
