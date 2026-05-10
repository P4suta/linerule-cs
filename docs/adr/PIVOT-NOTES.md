# Pivot notes — overlay startup is broken; next session picks up here

**State as of 2026-05-11 06:30**: `main` HEAD = `be48cae`. The overlay does NOT start in production — `bridge.Connect(island)` silently kills the process with no managed exception. Reproduce by running `artifacts/publish/linerule.exe`; the JSONL log stops at:

```
Bridge | bridge.Connect(island) about to call
```

…and the process exits.

## Why we got here (data-driven trail)

| iteration | HWND ex-style | Compositor | Island API | bridge | observable result |
|---|---|---|---|---|---|
| v1 (original) | `WS_EX_NOREDIRECTIONBITMAP` only | Windows.UI.Composition | `CreateForSystemVisual` | DesktopAttachedSiteBridge | runs; click-through silently dropped (HTTRANSPARENT goes nowhere because no redirection surface) |
| LWA_ALPHA test | `+ WS_EX_LAYERED + LWA_ALPHA` | same | same | same | runs; click-through works |
| v2 (current `main`) | `WS_EX_LAYERED` only (no NOREDIR) | **Microsoft.UI.Composition** | `ContentIsland.Create` | DesktopAttachedSiteBridge | `bridge.Connect` native wedge |

Key data point we missed in v1: the LWA_ALPHA test proved click-through works **with WS_EX_LAYERED + WS_EX_TRANSPARENT**, NOT with WS_EX_NOREDIRECTIONBITMAP alone. We over-pivoted in v2 by also dropping NOREDIRECTIONBITMAP and switching the compositor.

## Canonical pattern (PowerToys MouseHighlighter, production)

`microsoft/PowerToys` `src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp` ships exactly the use case we're chasing — transparent click-through overlay over the desktop. The pattern:

```cpp
// 1. HWND — both LAYERED *and* NOREDIRECTIONBITMAP, *together*
DWORD exStyle = WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW;
hwnd = CreateWindowExW(exStyle, ...);

// 2. Compositor — Windows.UI.Composition (system), NOT Microsoft.UI.Composition
m_compositor = winrt::Compositor();

// 3. Composition target — ICompositorDesktopInterop directly. NO ContentIsland, NO bridge.
ABI::IDesktopWindowTarget* target;
m_compositor.as<ABI::ICompositorDesktopInterop>()->CreateDesktopWindowTarget(m_hwnd, false, &target);

// 4. Root visual with size adjustment
m_root = m_compositor.CreateContainerVisual();
m_root.RelativeSizeAdjustment({1.0f, 1.0f});
m_target.Root(m_root);
```

## ICompositorDesktopInterop C# binding

From `microsoft/Windows.UI.Composition-Win32-Samples` `dotnet/WPF/AcrylicEffect/AcrylicEffect/CompositionHost.cs`:

```csharp
[ComImport]
[Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ICompositorDesktopInterop
{
    void CreateDesktopWindowTarget(IntPtr hwndTarget, bool isTopmost, out ICompositionTarget target);
}
```

QueryInterface from the Compositor object via `((object)compositor as ICompositorDesktopInterop)!`.

## Action plan for the next session

1. **Add `WS_EX_NOREDIRECTIONBITMAP` back** alongside the existing WS_EX_LAYERED + WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW + WS_EX_TOPMOST in `OverlayWindow.CreateHwnd`.

2. **Revert overlay rendering to `Windows.UI.Composition`**:
   - `using Windows.UI.Composition;` in CompositionRenderer + Hud/HudVisual + Hud/HudRenderer.
   - HudRenderer: `Windows.Graphics.SizeInt32` (not `Windows.Foundation.Size`), `Windows.Graphics.DirectX.DirectXPixelFormat / DirectXAlphaMode`, `Windows.UI.Colors`.

3. **Drop ContentIsland + DesktopAttachedSiteBridge** in `OverlayWindow.AttachBridgeAndIsland`. Replace with:
   ```csharp
   var compositor = new Windows.UI.Composition.Compositor();
   var interop = (ICompositorDesktopInterop)(object)compositor;
   interop.CreateDesktopWindowTarget(hwnd.Value, isTopmost: false, out var target);
   var root = compositor.CreateContainerVisual();
   root.RelativeSizeAdjustment = new Vector2(1f, 1f);
   target.Root = root;
   ```
   The overlay no longer holds a `DesktopAttachedSiteBridge` field; replace the `bridge`-typed field with the `DesktopWindowTarget`. `OverlayWindow.DisposeAsync` releases the target instead of the bridge.

4. **Remove `<UseWinUI>true</UseWinUI>`** from `Linerule.Platform.Windows.csproj` (revert to false). With the system Compositor, Win2D's `CanvasComposition.CreateCompositionGraphicsDevice(Windows.UI.Composition.Compositor, CanvasDevice)` is the legacy projection — works without UseWinUI.

5. **Update ADR-0009 to v3**: "WS_EX_LAYERED + WS_EX_NOREDIRECTIONBITMAP + Windows.UI.Composition + ICompositorDesktopInterop". The PowerToys pattern is the canonical click-through transparent overlay path. Microsoft.UI.Composition does NOT yet expose an interop equivalent for non-XAML hosts.

6. **Sanity check**: build, publish, run. Expected JSONL on the next user run:
   ```
   Composition | Windows.UI.Composition.Compositor created
   Composition | DesktopWindowTarget bound to HWND
   Composition | root visual attached
   OverlayWindow | ShowWindow ok — overlay live
   Hud | HUD visual attached
   ```

## Files to revert / rewrite (concrete)

- `src/Linerule.Platform.Windows/OverlayWindow.cs`
  - Restore WS_EX_NOREDIRECTIONBITMAP in CreateHwnd ex-style
  - Replace AttachBridgeAndIsland with AttachDesktopWindowTarget (uses ICompositorDesktopInterop)
  - Replace `_bridge` / `_island` fields with `_target` (DesktopWindowTarget)
  - DisposeAsync: dispose target, compositor (no bridge)
  - Update class docstring (this is now v3 of the architecture)
  - `using Windows.UI.Composition` (not Microsoft.UI.Composition)

- `src/Linerule.Platform.Windows/CompositionRenderer.cs`
  - `using Windows.UI.Composition;` (revert)

- `src/Linerule.Platform.Windows/Hud/HudVisual.cs`
  - `using Windows.UI.Composition;` (revert)

- `src/Linerule.Platform.Windows/Hud/HudRenderer.cs`
  - `using Windows.UI.Composition;` (revert)
  - `using Windows.Graphics;` for `SizeInt32`
  - `using Windows.Graphics.DirectX;`
  - `using Windows.UI;` for `Colors`
  - Drop `using Microsoft.UI;` / `using Microsoft.Graphics.DirectX;` / `using Windows.Foundation;` (Size)
  - `_graphicsDevice.CreateDrawingSurface(new SizeInt32 { Width = ..., Height = ... }, ...)`

- `src/Linerule.Platform.Windows/Linerule.Platform.Windows.csproj`
  - `<UseWinUI>false</UseWinUI>`

- `src/Linerule.Platform.Windows/NativeMethods.txt`
  - No re-add of LAYERED_WINDOW_ATTRIBUTES_FLAGS / SetLayeredWindowAttributes / COLORREF (we don't need them — Composition writes the surface, no LWA call needed; per-pixel alpha goes through DComp not DWM redirection).

- `docs/adr/0009-transparency-via-dcomp.md`
  - Rewrite as v3 with the PowerToys pattern.

## Subordinate task (#16)

After overlay actually starts: implement long-press auto-repeat for opacity / thickness hotkeys. Approach:
- After a registered hotkey fires, a polling timer reads `GetAsyncKeyState` for the chord
- While held: re-emit the action every ~50 ms with a ~250 ms initial delay (mirrors OS keyboard-repeat ergonomics)
- On release: stop polling
- Owns its lifetime via a `HotkeyRepeater` class in Linerule.Platform.Windows

Defer until the overlay starts cleanly.
