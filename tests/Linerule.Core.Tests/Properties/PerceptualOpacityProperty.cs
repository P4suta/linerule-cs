using FsCheck.Xunit;

namespace Linerule.Core.Tests.Properties;

public sealed class PerceptualOpacityProperty
{
    // Map an arbitrary float into the closed unit interval. FsCheck supplies
    // every range including NaN / ±∞ / huge values; the modulus-fold keeps
    // us inside [0, 1] without sacrificing the diversity of seeds.
    private static float ToUnit(float seed)
    {
        if (float.IsNaN(seed) || float.IsInfinity(seed))
        {
            return 0.5f;
        }
        var abs = MathF.Abs(seed) % 1.0001f;
        return MathF.Min(1f, abs);
    }

    [Property]
    public bool Smooth_output_stays_in_unit_interval(float seed)
    {
        var v = PerceptualOpacity.Smooth(seed);
        return v is >= 0f and <= 1f;
    }

    [Property]
    public bool Lstar_output_stays_in_unit_interval(float seed)
    {
        var v = PerceptualOpacity.Lstar(seed);
        return v is >= 0f and <= 1f;
    }

    [Property]
    public bool Smooth_is_monotone_non_decreasing(float a, float b)
    {
        var ax = ToUnit(a);
        var bx = ToUnit(b);
        if (ax > bx)
        {
            (ax, bx) = (bx, ax);
        }
        var lo = PerceptualOpacity.Smooth(ax);
        var hi = PerceptualOpacity.Smooth(bx);
        return hi >= lo;
    }

    [Property]
    public bool Lstar_is_monotone_non_decreasing(float a, float b)
    {
        var ax = ToUnit(a);
        var bx = ToUnit(b);
        if (ax > bx)
        {
            (ax, bx) = (bx, ax);
        }
        var lo = PerceptualOpacity.Lstar(ax);
        var hi = PerceptualOpacity.Lstar(bx);
        return hi >= lo;
    }

    [Property]
    public bool Smooth_dominates_linear_inside_open_interval(float seed)
    {
        // Stevens / sRGB curves bow above the identity for t ∈ (0, 1).
        // Endpoints (0 and 1) are pinned to the line, so we exclude them.
        var t = ToUnit(seed);
        return t is <= 0f or >= 1f || PerceptualOpacity.Smooth(t) > t;
    }

    [Property]
    public bool Lstar_dominates_linear_above_breakpoint(float seed)
    {
        // CIE L* matches Stevens on the perceptual-bow direction; below the
        // breakpoint the toe segment is *linear in t*, so the strict "> t"
        // claim only holds for t above the breakpoint.
        var t = ToUnit(seed);
        return t is <= 0.01f or >= 1f || PerceptualOpacity.Lstar(t) > t;
    }
}
