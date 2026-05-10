# ADR-0009: Transparency via DirectComposition (NOT color-key)

**Status**: Accepted
**Date**: 2026-05-11

## Context — and the counter-ADR to Rust 0009

The Rust side ships v0.1 with a workaround: `WS_EX_LAYERED` + `LWA_COLORKEY` with pure-black as the transparent key. The mask layer therefore can't use pure black `(0,0,0)` — it'd be keyed away — so `Rgba::DefaultMask = (8, 8, 8, 0xCC)`. That near-black is a lie embedded in the domain.

The lie exists because `wgpu`'s DX12 backend doesn't expose `IDCompositionDevice` / `CreateSwapChainForComposition`. Day-1 true per-pixel alpha needs DirectComposition, which Rust's chosen render stack can't reach.

C# / WinAppSDK does **not** have this problem. `Microsoft.UI.Composition.Compositor` is built on DirectComposition. `Compositor.CreateDesktopWindowTarget(WindowId, isTopmost)` gives true per-pixel alpha at the surface boundary.

## Decision

The C# overlay window opts out of the redirection bitmap (`WS_EX_NOREDIRECTIONBITMAP`) and uses a Composition target. No `WS_EX_LAYERED`. No `LWA_COLORKEY`. `Rgba.DefaultMask` is restored to pure black `(0, 0, 0, 0xCC)`.

Click-through still requires `WS_EX_TRANSPARENT` (cursor hit-test), `WS_EX_NOACTIVATE` (no focus steal), and `WS_EX_TOOLWINDOW` (no taskbar entry). The full ex-style set: `WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`.

## Verification

- Unit test `RgbaTests.DefaultMask_is_pure_black_not_near_black` asserts the architectural invariant.
- E2E §5 (drag-window-under-mask) verifies that the dim region is genuinely translucent — desktop content visible through the dim overlay, not a color-keyed cutout.

## Consequences

- This is the architectural payoff that justifies the C# rewrite. Without DComp, both implementations would carry the same `(8, 8, 8)` smell.
- The Rust v0.2 plan to lift to DComp via `IDCompositionDevice` directly remains valid for that side; not our concern.
