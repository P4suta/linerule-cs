using Linerule.Config;

namespace Linerule.Diagnostics;

/// <summary>
/// Diagnostic sink that drops everything. Useful for hot paths and tests
/// that don't care about the output stream.
/// </summary>
public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();

    private NullDiagnosticSink() { }

    public void Write(DiagnosticSeverity severity, string message, string? dotPath = null)
    {
        _ = severity;
        _ = message;
        _ = dotPath;
    }
}
