using System.CommandLine;
using Linerule.Platform.Windows;
using AppContext = Linerule.Diagnostics.Storage.AppContext;

namespace Linerule.Cli.Commands;

internal static class RunCommand
{
    public static Command Build(AppContext ctx)
    {
        var cmd = new Command("run", "Start the overlay (default).");
        cmd.SetAction((_, ct) => Execute(ctx, ct));
        return cmd;
    }

    // Shared between the explicit `linerule run` invocation and the
    // RootCommand default action — argless `linerule(.exe)` resolves here too,
    // matching the "(default)" promise in the run command's description.
    // ADR-0015: tunables are compile-time constants on UserConfig.Default,
    // sourced from ctx.Config; there is no runtime config file to override.
    internal static Task<int> Execute(AppContext ctx, CancellationToken cancellationToken) =>
        WindowsApp.RunAsync(ctx.Config, ctx.Logger, cancellationToken);
}
