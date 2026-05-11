using Linerule.Config;

namespace Linerule.Config.Tests;

/// <summary>
/// Boundary behavior of the generic <see cref="RangeValidator.InRange{T}"/>:
/// null silently falls back, exact boundaries succeed, beyond-boundary inputs
/// rejected with an Error diagnostic that names the dot-path, range, and
/// fallback. NaN is treated as out of range for IEEE-754 types.
/// </summary>
public sealed class RangeValidatorTests
{
    [Fact]
    public void IntInRangeReturnsValueWithEmptyBag()
    {
        var v = RangeValidator.InRange<int>(50, fallback: 10, "x.y", min: 0, max: 100, source: null);
        Assert.True(v.IsOk);
        Assert.Equal(50, v.Value);
        Assert.Equal(0, v.Errors.Count);
    }

    [Fact]
    public void IntNullFallsBackSilently()
    {
        var v = RangeValidator.InRange<int>(value: null, fallback: 10, "x.y", min: 0, max: 100, source: null);
        Assert.True(v.IsOk);
        Assert.Equal(10, v.Value);
        Assert.Equal(0, v.Errors.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void IntInclusiveBoundariesAccepted(int boundary)
    {
        var v = RangeValidator.InRange<int>(boundary, fallback: 50, "x.y", min: 0, max: 100, source: null);
        Assert.True(v.IsOk);
        Assert.Equal(boundary, v.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void IntOutOfRangeYieldsFallbackAndErrorDiagnostic(int outOfRange)
    {
        var v = RangeValidator.InRange<int>(outOfRange, fallback: 50, "x.y", min: 0, max: 100, source: "cfg.toml");
        Assert.False(v.IsOk);
        Assert.Equal(50, v.Value); // value collapses to fallback
        Assert.Equal(1, v.Errors.Count);
        var diag = v.Errors.Build()[0];
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("x.y", diag.DotPath);
        Assert.Equal("cfg.toml", diag.Source);
        Assert.NotNull(diag.Suggestion);
    }

    [Fact]
    public void LongHandledByGenericInstantiation()
    {
        var ok = RangeValidator.InRange<long>(100L, 0L, "p", 0L, 1_000_000_000_000L, source: null);
        Assert.True(ok.IsOk);
        Assert.Equal(100L, ok.Value);

        var bad = RangeValidator.InRange<long>(-1L, 42L, "p", 0L, 100L, source: null);
        Assert.False(bad.IsOk);
        Assert.Equal(42L, bad.Value);
    }

    [Fact]
    public void FloatNaNRejected()
    {
        var v = RangeValidator.InRange<float>(float.NaN, fallback: 1.0f, "p", 0.0f, 10.0f, source: null);
        Assert.False(v.IsOk);
        Assert.Equal(1.0f, v.Value);
    }

    [Fact]
    public void FloatInfinityRejected()
    {
        var v = RangeValidator.InRange<float>(float.PositiveInfinity, fallback: 1.0f, "p", 0.0f, 10.0f, source: null);
        Assert.False(v.IsOk);
    }

    [Fact]
    public void DoubleNaNRejected()
    {
        var v = RangeValidator.InRange<double>(double.NaN, fallback: 1.0, "p", 0.0, 10.0, source: null);
        Assert.False(v.IsOk);
        Assert.Equal(1.0, v.Value);
    }

    [Fact]
    public void DoubleNegativeOutOfRange()
    {
        var v = RangeValidator.InRange<double>(-0.0001, fallback: 0.5, "alpha", 0.0, 1.0, source: null);
        Assert.False(v.IsOk);
        Assert.Equal(0.5, v.Value);
        var diag = v.Errors.Build()[0];
        Assert.Equal("alpha", diag.DotPath);
    }
}
