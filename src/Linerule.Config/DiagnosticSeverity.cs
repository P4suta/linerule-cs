namespace Linerule.Config;

/// <summary>
/// Severity of a <see cref="ConfigDiagnostic"/>, ordered as a
/// bounded join-semilattice <c>Hint ⊑ Info ⊑ Warning ⊑ Error</c> with bottom
/// <see cref="Hint"/> and top <see cref="Error"/>. Numeric ordering matches
/// the lattice order, so <c>Math.Max((int)a, (int)b)</c> *is* the join (⊔),
/// and aggregation via <see cref="DiagnosticBag.Combine"/> collapses to a
/// single integer compare — see <see cref="SeverityLattice"/>.
///
/// <para>
/// Only <see cref="Error"/> causes <see cref="ConfigLoader"/> to return
/// <see cref="Result{T,E}.Err"/>; lower severities are informational and the
/// load still succeeds.
/// </para>
/// </summary>
public enum DiagnosticSeverity
{
    Hint = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}
