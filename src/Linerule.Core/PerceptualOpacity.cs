using System;

namespace Linerule.Core;

/// <summary>
/// Closed-form perceptual mappings for opacity ramps. Both functions take
/// linear input in <c>[0, 1]</c> and return perceptually-smoothed output in
/// <c>[0, 1]</c>, pinning the endpoints (<c>0 → 0</c>, <c>1 → 1</c>) exactly.
/// Allocations: none. Pure managed math, AOT-safe.
///
/// <para>
/// <b>Why a curve at all</b>: human brightness perception is not linear in
/// emitted light. Stevens's power law (psychophysics) models perceived
/// brightness as <c>I^a</c> with <c>a ≈ 1/2.2 .. 1/3</c> for the
/// fade-to-zero stimulus class, so a linear opacity ramp reads as a
/// "sudden snap" near zero and an "almost unchanged" mid-range. See
/// ADR-0016 for the design decision.
/// </para>
///
/// <para>
/// <b>Curve choice</b>:
/// <list type="bullet">
///   <item><see cref="Smooth"/> = sRGB-encoding inverse, the de facto
///     Stevens approximation. Soft enough for animated fades, no toe
///     segment needed because the visible duration of any single value
///     is short.</item>
///   <item><see cref="Lstar"/> = CIE L* (Lab lightness) piecewise:
///     cube-root above <c>(6/29)³ ≈ 0.008856</c>, linear toe below. The
///     toe kills the vertical tangent that a pure cube-root has at 0,
///     eliminating banding on large area fills (mask, indicator).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Primary sources</b>:
/// <list type="bullet">
///   <item>Stevens's power law — <c>https://en.wikipedia.org/wiki/Stevens%27s_power_law</c></item>
///   <item>sRGB EOTF — <c>https://en.wikipedia.org/wiki/SRGB</c></item>
///   <item>CIE L* — <c>https://en.wikipedia.org/wiki/CIELAB_color_space</c></item>
///   <item>Direct2D D2D1_GAMMA — <c>https://learn.microsoft.com/en-us/windows/win32/api/d2d1/ne-d2d1-d2d1_gamma</c></item>
/// </list>
/// </para>
/// </summary>
public static class PerceptualOpacity
{
    /// <summary>
    /// Primary curve for time-varying fades (e.g. the HUD cursor-distance
    /// fade). Returns <c>linear^(1/2.2)</c> clamped to <c>[0, 1]</c>.
    /// <para>
    /// Out-of-range and <c>NaN</c> inputs collapse to <c>0</c> on the
    /// "missing data" side and <c>1</c> on the "overflow" side, never
    /// throw.
    /// </para>
    /// </summary>
    public static float Smooth(float linear) =>
        linear switch
        {
            var x when float.IsNaN(x) || x <= 0f => 0f,
            >= 1f => 1f,
            _ => MathF.Pow(linear, 1f / 2.2f),
        };

    /// <summary>
    /// Secondary curve for large area fills (focus-mode mask, mode indicator
    /// stripe). CIE L* normalized to <c>[0, 1]</c>:
    /// <c>L*(linear) = (116·f(linear) − 16) / 100</c>, where
    /// <c>f(t) = t^(1/3)</c> for <c>t &gt; (6/29)³</c> and
    /// <c>f(t) = (29/6)²·t / 3 + 4/29</c> otherwise.
    /// <para>
    /// The two segments meet continuously at <c>t = (6/29)³ ≈ 0.008856</c>;
    /// the linear toe replaces the cube-root's vertical tangent at the
    /// origin, which is what would otherwise produce banding on slow,
    /// large-area fades.
    /// </para>
    /// </summary>
    public static float Lstar(float linear) =>
        linear switch
        {
            var x when float.IsNaN(x) || x <= 0f => 0f,
            >= 1f => 1f,
            _ => LstarBody(linear),
        };

    // CIE L* lab lightness inner function f(t), then scale by the
    // standard L* = 116·f − 16, normalized to [0, 1] by dividing by 100.
    // Breakpoint: t = (6/29)³ = 216/24389 ≈ 0.008856
    // Toe slope: (29/6)² / 3 = 841/108 ≈ 7.787
    // Toe intercept: 4/29 = 16/116 ≈ 0.1379 (so 116·intercept = 16, the
    // exact offset that L* subtracts — endpoints pin cleanly).
    private static float LstarBody(float linear)
    {
        const float breakpoint = 0.008856f;
        const float toeSlope = 7.787f;
        const float toeIntercept = 16f / 116f;
        var f = linear > breakpoint ? MathF.Pow(linear, 1f / 3f) : (toeSlope * linear) + toeIntercept;
        return ((116f * f) - 16f) / 100f;
    }
}
