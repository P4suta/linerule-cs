namespace Linerule.Diagnostics;

/// <summary>
/// Bounded join-semilattice for diagnostic severity:
/// <c>Hint ⊑ Info ⊑ Warning ⊑ Error</c>. Ordered so the maximum of two
/// severities is the more severe one (used to fold a stream of diagnostics
/// to a single exit-relevant level).
/// </summary>
public enum DiagnosticSeverity
{
    Hint = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}
