# ADR-0001: Tech stack

**Status**: Accepted
**Date**: 2026-05-11

## Context

linerule-cs is the C# / .NET dual of the Rust [linerule](../../../linerule/) project. The two implementations coexist; neither is the canonical one. C# was chosen for the Windows side because:

- `Microsoft.UI.Composition` rides DirectComposition. Day 1 true per-pixel alpha (see ADR-0009) — this is the architectural payoff.
- The `windows` Rust crate's churn around handle interop and `SetWindowDisplayAffinity` is gone.
- `wgpu` couldn't expose DComp for the Rust side; in C# / WinAppSDK the substrate is native.

## Decision

| Concern | Choice | Reason |
|---|---|---|
| Runtime | .NET 10 LTS | Active LTS, AOT improving, support window 2026–2028. |
| App framework | Windows App SDK 2.0 | The 2026 mainstream Windows desktop stack. |
| Render | `Microsoft.UI.Composition` (no XAML for the overlay HWND) | Visual tree IS the OverlayFrame ADT — minimal layer between domain and pixels. |
| Win32 P/Invoke | `Microsoft.Windows.CsWin32` source generator | Microsoft-supported, no hand-rolled DllImport. |
| Config | Tomlyn 2.x | Round-trip with the Rust `config.toml`; preserves comment trivia; surfaces span info. |
| CLI | `System.CommandLine` 2.x | Mainstream, AOT-aware. |
| Console UI | Spectre.Console | Squiggle-style diagnostic rendering. |
| Tests | xUnit v3 + FsCheck v3 + Verify v31 + Coverlet 10 | The 2026 .NET test pipeline. |

WinAppSDK's `Microsoft.UI.Composition.Compositor.CreateDesktopWindowTarget(WindowId, isTopmost: true)` is the documented way to host a Composition tree inside a non-XAML HWND. The bootstrapper (`Bootstrap.Initialize(0x00020000)`) is mandatory before any `Microsoft.UI.*` call (see also `WindowsAppRuntimeBootstrap`).

## Rejected alternatives

- **WPF**: viable, but the XAML compositor and `AllowsTransparency=true` are heavier than the bare HWND + Composition tree, and the architectural correspondence to OverlayFrame is muddied by `Rectangle` elements.
- **Avalonia**: cross-platform, but this is a Windows-only MVP; the cross-platform layer is overhead.
- **Bare Win32 + Direct2D + DComp via Vortice**: closest in spirit to the Rust stack, but Microsoft.UI.Composition gives the same result with less ceremony and is the supported path.

## Consequences

- WinAppSDK 2.0 minimum bound — the `Compositor` API surface is stable from 1.5 onward but 2.0 is the live mainstream.
- Native AOT support is uneven (see ADR-0010); the code is AOT-ready, but `<PublishAot>` is aspirational.
- Tomlyn 2.x is reflection-based; AOT compatibility verified at integration time.

## Verification at adoption time

NuGet versions in `Directory.Packages.props` are pinned from 2026-05-11 query results; reverify before bumping (memory: `feedback_verify_latest_versions`).
