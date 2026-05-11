using Linerule.Config;

namespace Linerule.Config.Tests;

/// <summary>
/// Monoid laws of <see cref="DiagnosticBag"/> under <see cref="DiagnosticBag.Combine"/>:
/// associativity with identity <see cref="DiagnosticBag.Empty"/>, plus running
/// join behavior of <see cref="DiagnosticBag.Severity"/>.
/// </summary>
public sealed class DiagnosticBagTests
{
    // Single allocation reused across asserts — satisfies CA1861 (prefer
    // static readonly over constant array arguments).
    private static readonly string[] CombineConcatMessages = ["a", "b", "c"];

    private static ConfigDiagnostic Diag(DiagnosticSeverity sev, string message = "x") =>
        new(message, Source: null, Span: null, Severity: sev);

    [Fact]
    public void EmptyBagStartsAtBottom()
    {
        var bag = DiagnosticBag.Empty;
        Assert.Equal(SeverityLattice.Bottom, bag.Severity);
        Assert.False(bag.IsFatal);
        Assert.Equal(0, bag.Count);
    }

    [Fact]
    public void AddUpdatesRunningJoinAndCount()
    {
        var bag = DiagnosticBag.Empty;
        bag.Add(Diag(DiagnosticSeverity.Info));
        Assert.Equal(DiagnosticSeverity.Info, bag.Severity);
        Assert.Equal(1, bag.Count);
        bag.Add(Diag(DiagnosticSeverity.Warning));
        Assert.Equal(DiagnosticSeverity.Warning, bag.Severity);
        bag.Add(Diag(DiagnosticSeverity.Hint));
        Assert.Equal(DiagnosticSeverity.Warning, bag.Severity);
        bag.Add(Diag(DiagnosticSeverity.Error));
        Assert.Equal(DiagnosticSeverity.Error, bag.Severity);
        Assert.True(bag.IsFatal);
        Assert.Equal(4, bag.Count);
    }

    [Fact]
    public void OfYieldsSingletonBag()
    {
        var bag = DiagnosticBag.Of(Diag(DiagnosticSeverity.Warning, "only"));
        Assert.Equal(1, bag.Count);
        Assert.Equal(DiagnosticSeverity.Warning, bag.Severity);
        Assert.Equal("only", bag.Build()[0].Message);
    }

    [Fact]
    public void CombineConcatenatesAndJoinsSeverity()
    {
        var left = DiagnosticBag.Empty;
        left.Add(Diag(DiagnosticSeverity.Hint, "a"));
        left.Add(Diag(DiagnosticSeverity.Info, "b"));
        var right = DiagnosticBag.Empty;
        right.Add(Diag(DiagnosticSeverity.Warning, "c"));
        var combined = DiagnosticBag.Combine(left, right);
        Assert.Equal(3, combined.Count);
        Assert.Equal(DiagnosticSeverity.Warning, combined.Severity);
        // Materialize into a typed IEnumerable<string> so IDE0305 (collection
        // simplification) is satisfied without triggering xUnit's
        // Span/T-overload ambiguity (which a bare `[.. select]` produces).
        IEnumerable<string> actual = combined.Build().Select(d => d.Message);
        Assert.Equal(CombineConcatMessages, actual);
    }

    [Fact]
    public void CombineWithEmptyIsIdentity()
    {
        var bag = DiagnosticBag.Empty;
        bag.Add(Diag(DiagnosticSeverity.Error, "boom"));
        var leftId = DiagnosticBag.Combine(DiagnosticBag.Empty, bag);
        var rightId = DiagnosticBag.Combine(bag, DiagnosticBag.Empty);
        Assert.Equal(bag.Count, leftId.Count);
        Assert.Equal(bag.Severity, leftId.Severity);
        Assert.Equal(bag.Count, rightId.Count);
        Assert.Equal(bag.Severity, rightId.Severity);
    }

    [Fact]
    public void CombineIsAssociative()
    {
        var a = DiagnosticBag.Of(Diag(DiagnosticSeverity.Hint, "a"));
        var b = DiagnosticBag.Of(Diag(DiagnosticSeverity.Warning, "b"));
        var c = DiagnosticBag.Of(Diag(DiagnosticSeverity.Info, "c"));
        var lhs = DiagnosticBag.Combine(DiagnosticBag.Combine(a, b), c);
        var rhs = DiagnosticBag.Combine(a, DiagnosticBag.Combine(b, c));
        Assert.Equal(lhs.Build().Select(d => d.Message), rhs.Build().Select(d => d.Message));
        Assert.Equal(lhs.Severity, rhs.Severity);
    }

    [Fact]
    public void CombineProducesFreshBag()
    {
        var a = DiagnosticBag.Of(Diag(DiagnosticSeverity.Info, "a"));
        var b = DiagnosticBag.Of(Diag(DiagnosticSeverity.Info, "b"));
        var combined = DiagnosticBag.Combine(a, b);
        combined.Add(Diag(DiagnosticSeverity.Error, "c"));
        Assert.Equal(1, a.Count);
        Assert.Equal(1, b.Count);
        Assert.Equal(DiagnosticSeverity.Info, a.Severity);
        Assert.Equal(DiagnosticSeverity.Info, b.Severity);
    }

    [Fact]
    public void IsFatalRespondsOnlyToError()
    {
        var bag = DiagnosticBag.Empty;
        bag.Add(Diag(DiagnosticSeverity.Warning));
        Assert.False(bag.IsFatal);
        bag.Add(Diag(DiagnosticSeverity.Error));
        Assert.True(bag.IsFatal);
    }
}
