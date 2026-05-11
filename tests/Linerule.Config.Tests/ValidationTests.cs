using Linerule.Config;

namespace Linerule.Config.Tests;

/// <summary>
/// Behaviour of <see cref="Validation{T}"/> as an applicative functor:
/// <see cref="Validation.Map"/> preserves bags, and the <c>ApplyN</c> family
/// concatenates diagnostics across independent branches without
/// short-circuiting.
/// </summary>
public sealed class ValidationTests
{
    private static ConfigDiagnostic Err(string m) =>
        new(m, Source: null, Span: null, Severity: DiagnosticSeverity.Error);

    private static ConfigDiagnostic Warn(string m) =>
        new(m, Source: null, Span: null, Severity: DiagnosticSeverity.Warning);

    [Fact]
    public void OkCarriesValueAndEmptyBag()
    {
        var v = Validation.Ok(42);
        Assert.True(v.IsOk);
        Assert.Equal(42, v.Value);
        Assert.Equal(0, v.Errors.Count);
    }

    [Fact]
    public void FailMakesIsOkFalseAndKeepsBag()
    {
        var bag = DiagnosticBag.Of(Err("bad"));
        var v = Validation.Fail<int>(bag);
        Assert.False(v.IsOk);
        Assert.Equal(1, v.Errors.Count);
    }

    [Fact]
    public void MapTransformsOnlyWhenOk()
    {
        var ok = Validation.Ok(10).Map(x => x * 2);
        Assert.True(ok.IsOk);
        Assert.Equal(20, ok.Value);

        var bag = DiagnosticBag.Of(Err("nope"));
        var fail = Validation.Fail<int>(bag).Map(x => x * 2);
        Assert.False(fail.IsOk);
    }

    [Fact]
    public void MapPropagatesWarningsThroughOkPath()
    {
        var warnBag = DiagnosticBag.Of(Warn("just a warning"));
        var v = new Validation<int>(7, warnBag).Map(x => x + 1);
        Assert.True(v.IsOk);
        Assert.Equal(8, v.Value);
        Assert.Equal(1, v.Errors.Count);
        Assert.Equal(DiagnosticSeverity.Warning, v.Errors.Severity);
    }

    [Fact]
    public void Apply2AccumulatesErrorsFromBothBranches()
    {
        var a = Validation.Fail<int>(DiagnosticBag.Of(Err("a")));
        var b = Validation.Fail<int>(DiagnosticBag.Of(Err("b")));
        var sum = Validation.Apply2(a, b, (x, y) => x + y);
        Assert.False(sum.IsOk);
        Assert.Equal(2, sum.Errors.Count);
    }

    [Fact]
    public void Apply2InvokesFunctionEvenOnFatalUsingFallbackOrDefault()
    {
        // Apply* always materialise: in the validation flow each branch
        // supplies a usable fallback (RangeValidator.InRange,
        // ValidateHudColors et al.), so the combining function is safe to
        // call regardless of severity. Failures surface through Errors.
        var a = Validation.Fail<int>(DiagnosticBag.Of(Err("a")));
        var b = Validation.Ok(5);
        var called = false;
        var sum = Validation.Apply2(
            a,
            b,
            (x, y) =>
            {
                called = true;
                return x + y;
            }
        );
        Assert.True(called);
        Assert.False(sum.IsOk);
        Assert.Equal(5, sum.Value); // 0 (default int) + 5
    }

    [Fact]
    public void Apply2CombinesWarningsWithoutBlockingValue()
    {
        var a = new Validation<int>(2, DiagnosticBag.Of(Warn("w1")));
        var b = new Validation<int>(3, DiagnosticBag.Of(Warn("w2")));
        var sum = Validation.Apply2(a, b, (x, y) => x + y);
        Assert.True(sum.IsOk);
        Assert.Equal(5, sum.Value);
        Assert.Equal(2, sum.Errors.Count);
    }

    [Fact]
    public void Apply3AccumulatesAcrossThreeBranches()
    {
        var a = Validation.Fail<int>(DiagnosticBag.Of(Err("a")));
        var b = Validation.Fail<int>(DiagnosticBag.Of(Err("b")));
        var c = Validation.Fail<int>(DiagnosticBag.Of(Err("c")));
        var sum = Validation.Apply3(a, b, c, (x, y, z) => x + y + z);
        Assert.False(sum.IsOk);
        Assert.Equal(3, sum.Errors.Count);
    }

    [Fact]
    public void Apply7CombinesSevenBranches()
    {
        var ok = Validation.Ok(1);
        var sum = Validation.Apply7(ok, ok, ok, ok, ok, ok, ok, (a, b, c, d, e, f, g) => a + b + c + d + e + f + g);
        Assert.True(sum.IsOk);
        Assert.Equal(7, sum.Value);
    }

    [Fact]
    public void IdentityLaw()
    {
        // Map(id) = id
        var v = Validation.Ok(99);
        var mapped = v.Map(x => x);
        Assert.Equal(v.Value, mapped.Value);
        Assert.Equal(v.Errors.Count, mapped.Errors.Count);
    }

    [Fact]
    public void CompositionLaw()
    {
        // Map(g . f) = Map(g) . Map(f)
        int f(int x) => x + 1;
        int g(int x) => x * 2;
        var v = Validation.Ok(3);
        var lhs = v.Map(x => g(f(x)));
        var rhs = v.Map(f).Map(g);
        Assert.Equal(lhs.Value, rhs.Value);
    }
}
