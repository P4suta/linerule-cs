using System.Globalization;
using Linerule.Config;
using Spectre.Console;

namespace Linerule.Cli.Diagnostics;

/// <summary>
/// User-facing renderer for <see cref="ConfigError"/>. Honors
/// <see cref="DiagnosticSeverity"/> color coding, attaches the dot-path,
/// and surfaces <see cref="ConfigDiagnostic.Suggestion"/> + Related so the
/// user can act on the diagnostic without reading the schema docs.
/// </summary>
internal static class DiagnosticPrinter
{
    public static int Render(ConfigError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        switch (error)
        {
            case ConfigError.FileSystem io:
                AnsiConsole.MarkupLineInterpolated($"[red]error[/]: cannot read [yellow]{io.Path}[/]: {io.Reason}");
                return 1;
            case ConfigError.SchemaDiagnostics d:
                var hasError = false;
                foreach (var diag in d.Items)
                {
                    RenderOne(diag);
                    if (diag.IsError)
                    {
                        hasError = true;
                    }
                }
                return hasError ? 1 : 0;
            default:
                AnsiConsole.MarkupLine("[red]error[/]: unknown config error");
                return 1;
        }
    }

    private static void RenderOne(ConfigDiagnostic diag)
    {
        var loc = diag.Span is { } s
            ? string.Create(CultureInfo.InvariantCulture, $"{diag.Source ?? "<config>"}:{s.Line}:{s.Column}")
            : diag.Source ?? "<config>";
        var (tag, color) = diag.Severity switch
        {
            DiagnosticSeverity.Error => ("error", "red"),
            DiagnosticSeverity.Warning => ("warning", "yellow"),
            DiagnosticSeverity.Info => ("info", "cyan"),
            DiagnosticSeverity.Hint => ("hint", "grey"),
            _ => ("error", "red"),
        };
        var dotPathLabel = string.IsNullOrEmpty(diag.DotPath) ? string.Empty : $"`{diag.DotPath}` ";
        AnsiConsole.MarkupLineInterpolated($"[{color}]{tag}[/] [grey]{loc}[/]: {dotPathLabel}{diag.Message}");
        if (!string.IsNullOrEmpty(diag.Suggestion))
        {
            AnsiConsole.MarkupLineInterpolated($"  [grey]suggestion:[/] {diag.Suggestion}");
        }
        if (diag.Related is { Count: > 0 } rel)
        {
            AnsiConsole.MarkupLineInterpolated($"  [grey]related:[/] {string.Join(", ", rel)}");
        }
    }
}
