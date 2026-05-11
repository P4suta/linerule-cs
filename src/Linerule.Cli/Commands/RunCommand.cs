using System.CommandLine;
using Linerule.Cli.Diagnostics;
using Linerule.Config;
using Linerule.Core;
using Linerule.Diagnostics;
using Linerule.Platform.Windows;
using AppContext = Linerule.Diagnostics.Storage.AppContext;

namespace Linerule.Cli.Commands;

internal static class RunCommand
{
    public static Command Build(AppContext ctx)
    {
        var cmd = new Command("run", "Start the overlay (default).");
        var configOption = new Option<string?>("--config", "-c")
        {
            Description = "Path to config.toml (default: %APPDATA%\\linerule\\config.toml).",
        };
        cmd.Options.Add(configOption);
        cmd.SetAction((parseResult, ct) => Execute(parseResult.GetValue(configOption), ctx, ct));
        return cmd;
    }

    // Shared between the explicit `linerule run [...]` invocation and the
    // RootCommand default action — argless `linerule(.exe)` resolves here too,
    // matching the "(default)" promise in the run command's description.
    // The AppContext carries the boot-resolved LoggerRoot + Config + sink so
    // the run path doesn't reload the file: BootDag.LoadConfig already did.
    internal static async Task<int> Execute(string? configPath, AppContext ctx, CancellationToken cancellationToken)
    {
        // Re-load only when the caller supplied an explicit --config that
        // differs from the boot DAG's default; otherwise reuse ctx.Config.
        UserConfig config;
        if (configPath is not null && File.Exists(configPath))
        {
            var loaded = ConfigLoader.Load(configPath);
            if (loaded is Result<UserConfig, ConfigError>.Err err)
            {
                return DiagnosticPrinter.Render(LineruleError.FromConfig(err.Error));
            }
            config = ((Result<UserConfig, ConfigError>.Ok)loaded).Value;
        }
        else
        {
            config = ctx.Config;
        }
        return await WindowsApp.RunAsync(config, ctx.Logger, cancellationToken).ConfigureAwait(false);
    }
}
