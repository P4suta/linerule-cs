using Linerule.Config;

namespace Linerule.Config.Tests;

public sealed class SeverityLatticeTests
{
    public static IEnumerable<TheoryDataRow<DiagnosticSeverity>> Severities() =>
        Enum.GetValues<DiagnosticSeverity>().Select(s => new TheoryDataRow<DiagnosticSeverity>(s));

    public static IEnumerable<TheoryDataRow<DiagnosticSeverity, DiagnosticSeverity>> Pairs()
    {
        foreach (var a in Enum.GetValues<DiagnosticSeverity>())
        {
            foreach (var b in Enum.GetValues<DiagnosticSeverity>())
            {
                yield return new TheoryDataRow<DiagnosticSeverity, DiagnosticSeverity>(a, b);
            }
        }
    }

    public static IEnumerable<TheoryDataRow<DiagnosticSeverity, DiagnosticSeverity, DiagnosticSeverity>> Triples()
    {
        foreach (var a in Enum.GetValues<DiagnosticSeverity>())
        {
            foreach (var b in Enum.GetValues<DiagnosticSeverity>())
            {
                foreach (var c in Enum.GetValues<DiagnosticSeverity>())
                {
                    yield return new TheoryDataRow<DiagnosticSeverity, DiagnosticSeverity, DiagnosticSeverity>(a, b, c);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Severities))]
    public void Idempotent(DiagnosticSeverity s) => Assert.Equal(s, SeverityLattice.Join(s, s));

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Commutative(DiagnosticSeverity left, DiagnosticSeverity right) =>
        // Renamed test-method parameters from (a, b) so SonarAnalyzer's S2234
        // (which fires when call-site argument names match the callee's
        // parameter names but in different order) doesn't misread the
        // by-construction swap that defines commutativity.
        Assert.Equal(SeverityLattice.Join(left, right), SeverityLattice.Join(right, left));

    [Theory]
    [MemberData(nameof(Triples))]
    public void Associative(DiagnosticSeverity a, DiagnosticSeverity b, DiagnosticSeverity c) =>
        Assert.Equal(
            SeverityLattice.Join(SeverityLattice.Join(a, b), c),
            SeverityLattice.Join(a, SeverityLattice.Join(b, c))
        );

    [Theory]
    [MemberData(nameof(Severities))]
    public void BottomIsIdentity(DiagnosticSeverity s) =>
        Assert.Equal(s, SeverityLattice.Join(SeverityLattice.Bottom, s));

    [Theory]
    [MemberData(nameof(Severities))]
    public void TopAbsorbs(DiagnosticSeverity s) =>
        Assert.Equal(SeverityLattice.Top, SeverityLattice.Join(SeverityLattice.Top, s));

    [Fact]
    public void EnumOrderingMatchesLattice()
    {
        Assert.Equal(DiagnosticSeverity.Hint, SeverityLattice.Bottom);
        Assert.Equal(DiagnosticSeverity.Error, SeverityLattice.Top);
        Assert.True((int)DiagnosticSeverity.Hint < (int)DiagnosticSeverity.Info);
        Assert.True((int)DiagnosticSeverity.Info < (int)DiagnosticSeverity.Warning);
        Assert.True((int)DiagnosticSeverity.Warning < (int)DiagnosticSeverity.Error);
    }
}
