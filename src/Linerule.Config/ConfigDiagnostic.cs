using System.Text;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// A single diagnostic raised while loading a config file.
/// <see cref="Cause"/> carries the typed <see cref="CoreError"/> for smart-constructor
/// rejections so tooling can pattern-match instead of substring-matching <see cref="Message"/>.
/// </summary>
public sealed record ConfigDiagnostic(
    string Message,
    string? Source,
    SourcePosition? Span,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    string? DotPath = null,
    string? Suggestion = null,
    IReadOnlyList<string>? Related = null,
    CoreError? Cause = null
)
{
    /// <summary>Whether this diagnostic is fatal (i.e. <see cref="Severity"/> = Error).</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Source ?? "<config>");
        if (Span is { } s)
        {
            sb.Append(':').Append(s.Line).Append(':').Append(s.Column);
        }
        sb.Append(": ").Append(SeverityLabel(Severity)).Append(": ");
        if (!string.IsNullOrEmpty(DotPath))
        {
            sb.Append('`').Append(DotPath).Append("` ");
        }
        sb.Append(Message);
        if (!string.IsNullOrEmpty(Suggestion))
        {
            sb.Append('\n').Append("  suggestion: ").Append(Suggestion);
        }
        if (Related is { Count: > 0 } rel)
        {
            sb.Append('\n').Append("  related: ").AppendJoin(", ", rel);
        }
        return sb.ToString();
    }

    private static string SeverityLabel(DiagnosticSeverity s) =>
        s switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Hint => "hint",
            _ => "error",
        };
}
