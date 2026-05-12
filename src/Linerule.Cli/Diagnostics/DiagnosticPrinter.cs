using Linerule.Bootstrap;
using Linerule.Diagnostics;
using Linerule.Platform;
using Spectre.Console;

namespace Linerule.Cli.Diagnostics;

/// <summary>
/// User-facing renderer for <see cref="LineruleError"/>. Folds the closed
/// coproduct into Spectre console output: chord parse, hotkey claim, core
/// smart-ctor, bootstrap phase, and unexpected runtime failures all emit a
/// single colored summary line. ADR-0015 retired the config-file path, so
/// the historic schema-diagnostic rendering no longer has a source.
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
}
