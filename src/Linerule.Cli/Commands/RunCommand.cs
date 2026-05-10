using System.CommandLine;
using Linerule.Cli.Diagnostics;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform.Windows;
using Spectre.Console;

namespace Linerule.Cli.Commands;

internal static class RunCommand
{
    public static Command Build()
    {
        var cmd = new Command("run", "Start the overlay (default).");
        cmd.SetAction(
            async (_, cancellationToken) =>
            {
                var path = ConfigLoader.DefaultPath();
                var loaded = File.Exists(path)
                    ? ConfigLoader.Load(path)
                    : Result.Ok<UserConfig, ConfigError>(UserConfig.Default);

                return loaded switch
                {
                    Result<UserConfig, ConfigError>.Ok ok => await WindowsApp
                        .RunAsync(ok.Value, cancellationToken)
                        .ConfigureAwait(false),
                    Result<UserConfig, ConfigError>.Err err => DiagnosticPrinter.Render(err.Error),
                    _ => 1,
                };
            }
        );
        return cmd;
    }
}
