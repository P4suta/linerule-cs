namespace Linerule.Config;

/// <summary>
/// Severity of a <see cref="ConfigDiagnostic"/>. Only <see cref="Error"/> causes
/// <see cref="ConfigLoader"/> to return <see cref="Result{T,E}.Err"/>; everything
/// else is informational and the load still succeeds.
/// </summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
    Hint,
}
