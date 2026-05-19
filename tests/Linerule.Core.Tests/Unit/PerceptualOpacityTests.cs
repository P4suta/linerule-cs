using System.Globalization;

namespace Linerule.Core.Tests.Unit;

public sealed class PerceptualOpacityTests
{
    // Numeric tolerance for non-trivial single-precision math (Pow with a
    // non-trivial exponent, cube-root, etc). 1e-3 is wide enough that the
    // accumulated single-precision error in MathF.Pow at 0.5^(1/2.2) doesn't
    // false-positive yet still tight enough to catch a wrong exponent or
    // a missed scaling factor.
    private const float Tolerance = 1e-3f;

    [Fact]
    public void Smooth_pins_zero()
    {
        Assert.Equal(0f, PerceptualOpacity.Smooth(0f));
    }

    [Fact]
    public void Smooth_pins_one()
    {
        Assert.Equal(1f, PerceptualOpacity.Smooth(1f));
    }

    [Fact]
    public void Smooth_midpoint_brightens_above_linear()
    {
        // pow(0.5, 1/2.2) ≈ 0.7297. The "midpoint reads brighter than 0.5"
        // property is the perceptual signature of the curve — if a future
        // refactor accidentally drops the exponent (e.g. ^1.0), this fails.
        var v = PerceptualOpacity.Smooth(0.5f);
        Assert.True(
            v > 0.5f,
            string.Create(CultureInfo.InvariantCulture, $"Smooth(0.5) = {v} must exceed linear midpoint")
        );
        Assert.InRange(v, 0.7297f - Tolerance, 0.7297f + Tolerance);
    }

    [Fact]
    public void Smooth_clamps_below_zero_to_zero()
    {
        Assert.Equal(0f, PerceptualOpacity.Smooth(-0.5f));
        Assert.Equal(0f, PerceptualOpacity.Smooth(float.NegativeInfinity));
    }

    [Fact]
    public void Smooth_clamps_above_one_to_one()
    {
        Assert.Equal(1f, PerceptualOpacity.Smooth(2f));
        Assert.Equal(1f, PerceptualOpacity.Smooth(float.PositiveInfinity));
    }

    [Fact]
    public void Smooth_treats_nan_as_zero()
    {
        // "Missing data" side. Throwing or producing NaN downstream would
        // immediately corrupt every alpha channel that consumes the result.
        Assert.Equal(0f, PerceptualOpacity.Smooth(float.NaN));
    }

    [Fact]
    public void Lstar_pins_zero()
    {
        Assert.Equal(0f, PerceptualOpacity.Lstar(0f));
    }

    [Fact]
    public void Lstar_pins_one()
    {
        Assert.Equal(1f, PerceptualOpacity.Lstar(1f));
    }

    [Fact]
    public void Lstar_midpoint_brightens_above_linear()
    {
        // L*(0.5) = (116·0.5^(1/3) − 16) / 100
        //        = (116·0.7937 − 16) / 100 ≈ 0.7607.
        var v = PerceptualOpacity.Lstar(0.5f);
        Assert.True(
            v > 0.5f,
            string.Create(CultureInfo.InvariantCulture, $"Lstar(0.5) = {v} must exceed linear midpoint")
        );
        Assert.InRange(v, 0.7607f - 1e-3f, 0.7607f + 1e-3f);
    }

    [Fact]
    public void Lstar_clamps_below_zero_to_zero()
    {
        Assert.Equal(0f, PerceptualOpacity.Lstar(-0.5f));
        Assert.Equal(0f, PerceptualOpacity.Lstar(float.NegativeInfinity));
    }

    [Fact]
    public void Lstar_clamps_above_one_to_one()
    {
        Assert.Equal(1f, PerceptualOpacity.Lstar(2f));
        Assert.Equal(1f, PerceptualOpacity.Lstar(float.PositiveInfinity));
    }

    [Fact]
    public void Lstar_treats_nan_as_zero()
    {
        Assert.Equal(0f, PerceptualOpacity.Lstar(float.NaN));
    }

    [Fact]
    public void Lstar_is_continuous_at_breakpoint()
    {
        // The cube-root and linear-toe segments must meet at (6/29)³.
        // Evaluating just-below (toe) and just-above (cube-root) at a
        // single-precision-friendly epsilon away from the breakpoint must
        // give values within a few ulps of each other.
        const float breakpoint = 0.008856f;
        const float eps = 1e-5f;
        var below = PerceptualOpacity.Lstar(breakpoint - eps);
        var above = PerceptualOpacity.Lstar(breakpoint + eps);
        Assert.InRange(above - below, -1e-3f, 1e-3f);
    }

    [Fact]
    public void Lstar_toe_segment_has_positive_slope_at_zero()
    {
        // The L* toe replaces the cube-root's vertical tangent at the origin.
        // A finite, positive slope at near-zero is the whole point — if
        // someone accidentally rewrites the toe as `0`, Lstar(tiny) collapses
        // to 0 and the mask loses its "barely-visible at low opacity" range.
        var a = PerceptualOpacity.Lstar(0.001f);
        var b = PerceptualOpacity.Lstar(0.002f);
        Assert.True(
            a > 0f,
            string.Create(CultureInfo.InvariantCulture, $"Lstar(0.001) = {a} must be positive (toe slope > 0)")
        );
        Assert.True(
            b > a,
            string.Create(CultureInfo.InvariantCulture, $"Lstar(0.002) = {b} must exceed Lstar(0.001) = {a}")
        );
    }
}
