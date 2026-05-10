# ADR-0001: Tech stack

**Status**: Accepted
**Date**: 2026-05-11

## Context

linerule-cs is the C# / .NET dual of the Rust [linerule](../../../linerule/) project. The two implementations coexist; neither is the canonical one. C# was chosen for the Windows side because:

- `Windows.UI.Composition` (the system Composition stack) rides DirectComposition. Day 1 true per-pixel alpha via the PowerToys `ICompositorDesktopInterop` pattern (see ADR-0009 v3) — this is the architectural payoff.
- The `windows` Rust crate's churn around handle interop and `SetWindowDisplayAffinity` is gone.
- `wgpu` couldn't expose DComp for the Rust side; in C# / WinAppSDK the substrate is native.

## Decision

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 LTS | Active LTS, AOT improving, support window 2026–2028. |
| App framework | Windows App SDK 2.0 | The 2026 mainstream Windows desktop stack. |
| Render | `Windows.UI.Composition` via `ICompositorDesktopInterop` (PowerToys MouseHighlighter pattern, no XAML, no `ContentIsland`) | Visual tree IS the OverlayFrame ADT — minimal layer between domain and pixels. |
| Win32 P/Invoke | `Microsoft.Windows.CsWin32` source generator | Microsoft-supported, no hand-rolled DllImport. |
| Config | Tomlyn 2.x | Round-trip with the Rust `config.toml`; preserves comment trivia; surfaces span info. |
| CLI | `System.CommandLine` 2.x | Mainstream, AOT-aware. |
| Console UI | Spectre.Console | Squiggle-style diagnostic rendering. |
| Tests | xUnit v3 + FsCheck v3 + Verify v31 + Coverlet 10 | The 2026 .NET test pipeline. |

`Windows.UI.Composition.Compositor` + `ICompositorDesktopInterop.CreateDesktopWindowTarget(HWND, isTopmost, out DesktopWindowTarget)` is the production-tested way (PowerToys, `Windows.UI.Composition-Win32-Samples`) to host a Composition tree inside a non-XAML HWND with `WS_EX_LAYERED`. The WinAppSDK bootstrapper (`Bootstrap.Initialize(0x00020000)`, wrapped by `WindowsAppRuntimeBootstrap`) is still required — Win2D 1.4 and `Microsoft.UI.Dispatching.DispatcherQueueController` both live in the WinAppSDK projection and need the runtime bootstrapped before first use.

## Rejected alternatives

- **WPF**: viable, but the XAML compositor and `AllowsTransparency=true` are heavier than the bare HWND + Composition tree, and the architectural correspondence to OverlayFrame is muddied by `Rectangle` elements.
- **Avalonia**: cross-platform, but this is a Windows-only MVP; the cross-platform layer is overhead.
- **Bare Win32 + Direct2D + DComp via Vortice**: closest in spirit to the Rust stack, but the `Windows.UI.Composition` + `ICompositorDesktopInterop` pattern PowerToys ships gives the same result with less ceremony and is the production-tested path.

## Consequences

- WinAppSDK 2.0 minimum bound — the `Compositor` API surface is stable from 1.5 onward but 2.0 is the live mainstream.
- Native AOT support is uneven (see ADR-0010); the code is AOT-ready, but `<PublishAot>` is aspirational.
- Tomlyn 2.x is reflection-based; AOT compatibility verified at integration time.

## Verification at adoption time

NuGet versions in `Directory.Packages.props` are pinned from 2026-05-11 query results; reverify before bumping (memory: `feedback_verify_latest_versions`).
