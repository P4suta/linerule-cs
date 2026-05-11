using System.Globalization;
using Linerule.Bootstrap;
using Linerule.Config;
using Linerule.Diagnostics;
using Linerule.Platform;
using Spectre.Console;

namespace Linerule.Cli.Diagnostics;

/// <summary>
/// User-facing renderer for <see cref="LineruleError"/>. Folds the closed
/// coproduct into Spectre console output: <c>ConfigError.SchemaDiagnostics</c>
/// gets the rich form (source span, dot-path, suggestion, related paths);
/// the other variants (chord parse, hotkey claim, core smart-ctor, runtime)
/// emit a single colored summary line.
/// </summary>
internal static class DiagnosticPrinter
{
    /// <summary>
    /// Render the boundary error and return the conventional process exit
    /// code as defined by <see cref="LineruleError.ToExitCode"/>.
    /// </summary>
    public static int Render(LineruleError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        switch (error)
        {
            case LineruleError.ConfigFault { Inner: ConfigError.FileSystem io }:
                AnsiConsole.MarkupLineInterpolated($"[red]error[/]: cannot read [yellow]{io.Path}[/]: {io.Reason}");
                break;
            case LineruleError.ConfigFault { Inner: ConfigError.SchemaDiagnostics d }:
                foreach (var diag in d.Items)
                {
                    RenderConfigDiagnostic(diag);
                }
                break;
            case LineruleError.Chord { Inner: var chord }:
                AnsiConsole.MarkupLineInterpolated($"[red]error[/]: chord parse failed: {chord.ToHumanString()}");
                break;
            case LineruleError.Hotkey { Inner: HotkeyError.AlreadyClaimed claim }:
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]error[/]: hotkey already claimed by another window: [yellow]{claim.Chord}[/]"
                );
                break;
            case LineruleError.Hotkey { Inner: HotkeyError.OsRefused os }:
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]error[/]: OS refused hotkey registration (HRESULT [yellow]0x{os.Hresult:X8}[/]): {os.Chord}"
                );
                break;
            case LineruleError.CoreFault { Inner: var coreErr }:
                AnsiConsole.MarkupLineInterpolated($"[red]error[/]: {coreErr.ToHumanString()}");
                break;
            case LineruleError.BootFault { Inner: BootstrapError.PhaseFailed pf }:
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]error[/]: bootstrap phase [yellow]{pf.PhaseName}[/] failed: {pf.Reason}"
                );
                break;
            case LineruleError.BootFault { Inner: BootstrapError.Threw th }:
                AnsiConsole.MarkupLineInterpolated(
                    $"[red]error[/]: bootstrap phase [yellow]{th.PhaseName}[/] threw: {th.Cause.Message}"
                );
                break;
            case LineruleError.BootFault { Inner: BootstrapError.Cancelled cn }:
                AnsiConsole.MarkupLineInterpolated(
                    $"[yellow]warning[/]: bootstrap phase [yellow]{cn.PhaseName}[/] cancelled"
                );
                break;
            case LineruleError.Unexpected r:
                AnsiConsole.MarkupLineInterpolated($"[red]error[/]: {r.Where}: {r.Cause?.Message ?? "<unknown>"}");
                break;
            default:
                AnsiConsole.MarkupLine("[red]error[/]: unknown linerule error");
                break;
        }
        return error.ToExitCode();
    }

    /// <summary>Back-compat helper used by tests that still hold a <see cref="ConfigError"/>.</summary>
    public static int Render(ConfigError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Render(LineruleError.FromConfig(error));
    }

    private static void RenderConfigDiagnostic(ConfigDiagnostic diag)
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
