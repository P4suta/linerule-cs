# ADR-0016: Perceptual opacity curves at the render boundary

**Status**: Accepted
**Date**: 2026-05-20

## Context

Every opacity value in linerule-cs (HUD cursor-distance fade, focus-mode mask alpha, mode-indicator stripe alpha) was linear: the domain stored `byte` or `float` in `[0..255]` / `[0,1]`, the render code passed it through `byte / 255f → D2D1_COLOR_F.a` unchanged, and the user-facing `BumpOpacity` hotkey moved the byte by a constant delta. First-hardware playthrough exposed two symptoms of this:

1. **HUD fade has a knee near zero**. The exponential ramp `1 − exp(−d/τ)` already curves at the *physical* level, but the perceived brightness ramp is steeper than linear in the same region — `d ≈ 0..τ/3` reads as "still fully visible", then `d ≈ τ` snaps to "almost gone".
2. **Mask `Opacity` slider steps feel uneven**. A linear 50 → 100 → 150 → 200 step pattern reads to the eye as "big jump → small jump → almost no jump". The user adjusts in 5-byte increments and gets visually-non-uniform results.

Both symptoms are textbook Stevens's power law:

> **Stevens's power law** (https://en.wikipedia.org/wiki/Stevens%27s_power_law) — perceived intensity ψ(I) scales as `k·I^a`. For brightness, the experimentally-fit exponent is ≈ 0.33–0.5 depending on stimulus class and dark-adaptation state. A linear ramp in `I` is therefore a non-linear ramp in ψ, with the steepest perceptual gradient at the low end.

Weber-Fechner's logarithmic law was considered and rejected: it diverges as `I → 0`, and Wikipedia explicitly notes it "is completely inapplicable at low light levels" — exactly where a fade-to-zero spends most of its visible range. CIE L* (https://en.wikipedia.org/wiki/CIELAB_color_space) is a closed-form refinement of Stevens with a built-in linear toe to kill the cube-root's vertical tangent at the origin.

Direct2D itself acknowledges this on the rendering side. `D2D1_GAMMA` (https://learn.microsoft.com/en-us/windows/win32/api/d2d1/ne-d2d1-d2d1_gamma) documents that sRGB blending uses gamma 2.2 and that "interpolating in a linear gamma space ... can avoid changes in perceived brightness caused by the effect of gamma correction." The alpha channel itself is linear in our pre-multiplied dcomp surfaces, but the displayed luminance is gamma-encoded by the framebuffer, so a perceptual fade *needs* to pre-warp the alpha value before it lands at the pixel.

## Decision

Introduce a small static helper, `PerceptualOpacity`, at `src/Linerule.Core/PerceptualOpacity.cs`, exposing two closed-form curves:

- **`Smooth(float linear) → float`** — sRGB-encoding inverse (`linear^(1/2.2)`), the de-facto Stevens approximation for animated UI fades. Used by `HudFadeKernel.ComputeOpacity` to reshape the exponential ramp output.
- **`Lstar(float linear) → float`** — CIE L* (Lab lightness) normalized to `[0, 1]`, piecewise: cube-root above the breakpoint `(6/29)³ ≈ 0.008856`, linear toe `7.787·t + 16/116` below. Used by large-area fills (mask + indicator) where the toe segment kills the vertical tangent at `t = 0` and prevents banding on slow fades over wide surfaces.

Both functions:
- Pin endpoints exactly (`0 → 0`, `1 → 1`).
- Treat `NaN` / `< 0` as `0`, `> 1` / `+∞` as `1` (no throws on degenerate input).
- Allocate nothing, AOT-safe, single-pass, closed-form.

The curves are applied at the **domain-to-render boundary** only:

- `HudFadeKernel.ComputeOpacity` composes `Smooth` with the existing exponential, returning a perceptually-smoothed `[0, 1]` to `HudVisual.SetOpacity`.
- `Opacity` gains a `ToPerceptualByte()` method that runs `Lstar` over `Value / 255f` and rounds back to byte. `Render.cs` uses this method when constructing every mask and indicator `Rgba.WithAlpha(...)`.
- `Opacity` also gains an `IndicatorDefault` static singleton (raw `0x80`, the historic indicator-stripe alpha) so the indicator path becomes a regular `Opacity.ToPerceptualByte()` call rather than a hard-coded byte literal.

What we intentionally do **not** change:

- `Rgba.WithAlpha(byte)` and `Rgba.A / 255f` in `CompositionRenderer.FillSurface` / `HudRenderer.ToColorF` stay linear. Low-level color math is the neutral pipe — it never decides "should this be brighter". Domain types (`Opacity`, `HudFadeKernel`) own the perceptual mapping.
- `HudVisual.SetOpacity`'s `clamped * _baseOpacity` stays a plain float multiply. `BaseOpacity` is a user-tunable constant; the perceptual mapping is already baked into the fade kernel that produces `clamped`.
- No double-mapping: an opacity value that flows through `Opacity.ToPerceptualByte()` (`Lstar`) does not also get `Smooth`. The two curves are sister functions for different stimulus categories, not a pipeline.

## Consequences

- **Visible**: every cursor-proximity fade and every `BumpOpacity` step lands at a perceptually-uniform position. The original "linear knee" complaint is removed at the math level.
- **Tests**: `tests/Linerule.Core.Tests/Unit/PerceptualOpacityTests.cs` + `Properties/PerceptualOpacityProperty.cs` pin endpoints, midpoint, monotonicity (FsCheck), toe continuity, and out-of-range handling for both curves. `RenderTests` updates its existing alpha-pass-through assertion to assert against `Opacity.ToPerceptualByte()` rather than the raw byte; the two Verify snapshots in `tests/Linerule.Core.Tests/Snapshot/RenderSnapshot.*` are regenerated with the curved alpha values (170 → 218 for default mask, 128 → 194 for indicator).
- **AOT / dcomp path is unaffected**: `Rgba` / `D2D1_COLOR_F` / `IDCompositionEffectGroup.SetOpacity` see the same byte / float types they always have; the values inside those types are just curved earlier in the pipeline. ADR-0009 (dcomp-direct) and ADR-0010 (AOT readiness) remain in force unchanged.
- **Reversibility**: if a future redesign wants linear semantics back (e.g. for a calibration tool that maps stimulus → response), `Opacity.Value` is still the raw user-facing byte; only `ToPerceptualByte()` applies the curve. Switching back is a 6-line revert.

## Alternatives considered (and rejected)

- **Weber-Fechner (log)**: diverges at 0, not a fade primitive.
- **Material Design `cubic-bezier(0.4, 0, 0.2, 1)`** (https://m3.material.io/styles/motion/easing-and-duration/tokens-specs): temporal easing for animations, not a perceptual brightness mapping. Composes with our curves rather than replaces them — orthogonal concerns.
- **WPF `CubicEase` / `PowerEase`** (https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.cubicease): WPF's `PowerEase` family is structurally Stevens; we just inline the math.
- **Run blending in linear gamma (`D2D1_GAMMA_1_0`)**: would shift the responsibility into dcomp/D2D but the per-surface gamma switch is a global render-target setting, not per-color, and would force us to convert every other color (HUD text, mask) too. Pre-warping at the domain boundary is a smaller blast radius.

## References

- Stevens's power law — https://en.wikipedia.org/wiki/Stevens%27s_power_law
- Weber-Fechner law — https://en.wikipedia.org/wiki/Weber%E2%80%93Fechner_law
- CIE L\* lightness — https://en.wikipedia.org/wiki/CIELAB_color_space
- sRGB EOTF — https://en.wikipedia.org/wiki/SRGB
- Direct2D `D2D1_GAMMA` — https://learn.microsoft.com/en-us/windows/win32/api/d2d1/ne-d2d1-d2d1_gamma
- Direct2D supported pixel formats and alpha modes — https://learn.microsoft.com/en-us/windows/win32/direct2d/supported-pixel-formats-and-alpha-modes
- Material Design motion easing — https://m3.material.io/styles/motion/easing-and-duration/tokens-specs
- MDN `cubic-bezier()` — https://developer.mozilla.org/en-US/docs/Web/CSS/easing-function
