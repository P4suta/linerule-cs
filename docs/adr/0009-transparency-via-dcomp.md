# ADR-0009: Transparency + click-through via WS_EX_LAYERED + WS_EX_NOREDIRECTIONBITMAP + Windows.UI.Composition + ICompositorDesktopInterop

**Status**: Accepted (v3 — supersedes v2's WS_EX_LAYERED-only / Microsoft.UI.Composition path)
**Date**: 2026-05-11

## v1 (deprecated) — what we tried first

- HWND created with `WS_EX_NOREDIRECTIONBITMAP` alone so DComp drew straight to DWM with no redirection surface.
- `Windows.UI.Composition.Compositor` + `ContentIsland.CreateForSystemVisual`.
- `bridge.ProcessesPointerInput = false` + `WS_EX_TRANSPARENT` + `WM_NCHITTEST → HTTRANSPARENT` for click-through.

**Verified empirically broken (2026-05-11).** Diagnostic logging proved:
- Our WndProc IS reached (`WM_NCHITTEST` count climbs ~55/sec).
- We DO return `HTTRANSPARENT`.
- Click `WM_LBUTTONDOWN` never reaches our WndProc.
- BUT clicks also never reach the underlying app (Notepad / Edge / Explorer).

The `WS_EX_NOREDIRECTIONBITMAP` window has no redirection surface for DWM to use as the cross-process hit-test target; DWM has nothing to route the click through. The clicks land in nothing. We isolated the redirection-bitmap presence as the load-bearing constraint by swapping to `WS_EX_LAYERED + LWA_ALPHA` — clicks immediately worked.

## v2 (deprecated) — what came next

- HWND: `WS_EX_LAYERED | WS_EX_TRANSPARENT` (NO `WS_EX_NOREDIRECTIONBITMAP`).
- `Microsoft.UI.Composition.Compositor` + `ContentIsland.Create(Visual)` + `DesktopAttachedSiteBridge.CreateFromWindowId(queue, windowId).Connect(island)`.

**Verified empirically broken (2026-05-11).** With diagnostic try/catch wrapping `bridge.Connect(island)` (commit `be48cae`), the published exe logs:

```
Bridge | bridge.Connect(island) about to call
```

…and the process exits — no managed exception, no crash dump, no further JSONL. Native wedge inside `DesktopAttachedSiteBridge.Connect` when the target HWND has `WS_EX_LAYERED`.

`Microsoft.UI.Composition.Compositor` does NOT expose `ICompositorDesktopInterop`. Its attachment story is `ContentIsland.Create` + `DesktopAttachedSiteBridge.Connect` (the WinUI 3 islands path); that combination ships fine on non-layered HWNDs (cf. `microsoft/WindowsAppSDK-Samples` `Samples/Islands/UXFrameworksOnIslands/TopLevelWindow.cpp` — `dwExStyle = 0`) but is empirically broken for `WS_EX_LAYERED` HWNDs in WinAppSDK 2.0.

## v3 (accepted) — canonical PowerToys pattern

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

- **Production reference**: `microsoft/PowerToys` `src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp` ships exactly the use case we're chasing — transparent click-through overlay over the desktop — with the same pattern.
- **Official C# binding**: `microsoft/Windows.UI.Composition-Win32-Samples` `dotnet/WPF/AcrylicEffect/CompositionHost.cs` is the Microsoft sample for the C# COM-import of `ICompositorDesktopInterop`.
- **Empirical bisection (this repo, 2026-05-11)**: v1 failed cross-process click-through; v2 failed native bridge.Connect on `WS_EX_LAYERED`; v3 is the remaining combination, validated against production PowerToys.
- **Microsoft official docs (DirectComposition / Basic Concepts)**: "the composition target window can be a layered window; that is, it can have the WS_EX_LAYERED window style."

## What this also unlocks

- **HUD rendering pipeline (Microsoft-pure)**: `Windows.UI.Composition.CompositionDrawingSurface` → `ICompositionDrawingSurfaceInterop.BeginDraw(IID_ID2D1DeviceContext)` → `ID2D1DeviceContext`, direct Direct2D + DirectWrite drawing into the same composition target as the overlay. Direct2D / DirectWrite / Direct3D11 / DXGI types are source-gen'd by `Microsoft.Windows.CsWin32` from `NativeMethods.txt`. Third-party D2D wrappers (Win2D, Vortice, SharpDX, …) are out — Microsoft official (CsWin32 + win32metadata + CsWinRT) is the entire interop story. The HUD shares the overlay's compositor: one HWND, one visual tree, no GDI fallback.
- **No special DispatcherQueue plumbing**: `Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread` (already established by `WindowsApp.RunCoreAsync` for the dispatcher-queue timer) sets up a CoreMessaging dispatcher that satisfies `Windows.UI.Composition.Compositor`'s requirement. No `Windows.System.DispatcherQueueController` overlay needed.
- **No `Microsoft.Graphics.Win2D` dependency**: the WinAppSDK-bound Win2D was the previous HUD path but it hard-couples to `Microsoft.UI.Composition.Compositor` and refuses `Windows.UI.Composition.Compositor` (verified 2026-05-11 with `CS1503` build errors). Dropping it removes the only third-party graphics package from the Platform.Windows assembly.

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

- **Microsoft.UI.Composition + ContentIsland + DesktopAttachedSiteBridge** (v2): empirically wedges natively on `WS_EX_LAYERED` HWNDs (verified 2026-05-11). The "modern" path Microsoft markets but it does not yet cover the click-through transparent overlay use case.
- **`WS_EX_NOREDIRECTIONBITMAP` alone + DComp + a second HWND for the HUD with GDI text** (v1 retreat): two windows + GDI is the architectural retreat the user explicitly rejected.
- **Microsoft.Graphics.Win2D**: WinAppSDK 2.0 / Microsoft.UI.Composition に hard-bind されており、`Windows.UI.Composition.Compositor` を受け取らない (CS1503 / CS0103 で build fail、2026-05-11 検証)。HUD 用途で `UseWinUI=false` にしても type projection は変わらない。
- **Vortice.Windows (Direct2D1 + Direct3D11)**: 同じ機能を提供する .NET binding として mainstream だが third-party。Microsoft 純粋路線 (CsWin32 + win32metadata) で同等の source-gen が得られるため、不要な OSS 依存として却下。

## References

- `microsoft/PowerToys` `src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp`
- `microsoft/Windows.UI.Composition-Win32-Samples` `dotnet/WPF/AcrylicEffect/CompositionHost.cs`
- Empirical evidence (JSONL): end-to-end runs 2026-05-11 (v1 click-drop, v2 bridge.Connect wedge)
- Microsoft docs (DirectComposition basic concepts; `ICompositorDesktopInterop` API reference)
- Auto-memory: `feedback_winappsdk_clickthrough_overlay`
- Plan file: `~/.claude/plans/repository-state-compressed-conway.md`
