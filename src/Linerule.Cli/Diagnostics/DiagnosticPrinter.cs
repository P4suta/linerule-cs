using Linerule.Config;
using Spectre.Console;

namespace Linerule.Cli.Diagnostics;

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
                foreach (var diag in d.Items)
                {
                    var loc = diag.Span is { } s ? $"{diag.Source ?? "<config>"}:{s.Line}:{s.Column}" : diag.Source ?? "<config>";
                    AnsiConsole.MarkupLineInterpolated($"[red]error[/] [grey]{loc}[/]: {diag.Message}");
                }

                return 1;
            default:
                AnsiConsole.MarkupLine("[red]error[/]: unknown config error");
                return 1;
        }
    }
}
