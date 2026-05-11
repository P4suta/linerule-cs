using System.Text;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// A single diagnostic raised while loading a config file.
///
/// <para><b>Fields</b>:</para>
/// <list type="bullet">
///   <item><see cref="Message"/> — human-readable primary text.</item>
///   <item><see cref="Source"/> — config file path (or null when parsing in-memory strings).</item>
///   <item><see cref="Span"/> — optional 1-based line/column for editor underlining.</item>
///   <item><see cref="Severity"/> — Error / Warning / Info / Hint. Error is the only fatal severity.</item>
///   <item><see cref="DotPath"/> — schema dot-path (e.g. <c>hud.padding.edge</c>) the diagnostic refers to.</item>
///   <item><see cref="Suggestion"/> — actionable fix hint surfaced to the user under the primary message.</item>
///   <item><see cref="Related"/> — other dot-paths involved in the same invariant (e.g. cross-field checks).</item>
///   <item><see cref="Cause"/> — typed root cause when the diagnostic was lifted from a <c>Linerule.Core</c>
///         smart-constructor rejection (Opacity / Thickness). Lets tooling pattern-match on the original
///         <see cref="CoreError"/> variant instead of substring-matching the rendered <see cref="Message"/>.</item>
/// </list>
///
/// <para>
/// Construction stays backward-compatible: existing call sites passing only
/// <c>(Message, Source, Span)</c> default to <see cref="DiagnosticSeverity.Error"/>
/// with no <see cref="DotPath"/> / <see cref="Suggestion"/> / <see cref="Related"/> / <see cref="Cause"/>.
/// </para>
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
