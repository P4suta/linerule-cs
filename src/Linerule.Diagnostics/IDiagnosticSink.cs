namespace Linerule.Diagnostics;

/// <summary>
/// Renderer-agnostic sink for one-line diagnostic output. Concrete
/// implementations route to a console (Spectre), to the structured event
/// store (Sqlite), or to memory for tests; <see cref="LineruleError.Render"/>
/// emits its content through this interface so the boundary code stays
/// terminal-agnostic.
/// </summary>
public interface IDiagnosticSink
{
    /// <summary>Emit a single diagnostic line.</summary>
    /// <param name="severity">Severity from the bounded join-semilattice <c>Hint ⊑ Info ⊑ Warning ⊑ Error</c>.</param>
    /// <param name="message">Human-readable primary text.</param>
    /// <param name="dotPath">Optional schema dot-path (e.g. <c>hud.padding.edge</c>).</param>
    void Write(DiagnosticSeverity severity, string message, string? dotPath = null);
}
