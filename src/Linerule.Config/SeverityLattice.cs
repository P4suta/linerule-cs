namespace Linerule.Config;

/// <summary>
/// Algebra of <see cref="DiagnosticSeverity"/> as a bounded join-semilattice.
///
/// <para>
/// The carrier set is the four severities ordered <c>Hint ⊑ Info ⊑ Warning ⊑ Error</c>.
/// <see cref="Join"/> is the binary least-upper-bound (⊔), <see cref="Bottom"/>
/// is the identity element. Together they form a commutative idempotent monoid
/// (<c>(DiagnosticSeverity, ⊔, Hint)</c>), which is precisely what
/// <see cref="DiagnosticBag"/> uses to maintain a running maximum severity in
/// O(1) per insertion.
/// </para>
/// </summary>
public static class SeverityLattice
{
    /// <summary>
    /// Least upper bound (join, ⊔) of two severities. Idempotent
    /// (<c>Join(s, s) = s</c>), commutative, associative.
    /// </summary>
    public static DiagnosticSeverity Join(DiagnosticSeverity a, DiagnosticSeverity b) =>
        (DiagnosticSeverity)Math.Max((int)a, (int)b);

    /// <summary>
    /// Identity element of <see cref="Join"/>: <c>Join(Bottom, s) = s</c> for all <c>s</c>.
    /// </summary>
    public static DiagnosticSeverity Bottom => DiagnosticSeverity.Hint;

    /// <summary>
    /// Top element: <c>Join(Top, s) = Top</c> for all <c>s</c>. Useful for
    /// short-circuit predicates like "is anything fatal here".
    /// </summary>
    public static DiagnosticSeverity Top => DiagnosticSeverity.Error;
}
