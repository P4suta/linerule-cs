using System.CommandLine;
using System.Diagnostics;
using Linerule.Cli.Diagnostics;
using Linerule.Config;
using Linerule.Core;
using Spectre.Console;

namespace Linerule.Cli.Commands;

internal static class ConfigCommand
{
    public static Command Build()
    {
        var cmd = new Command("config", "Inspect or edit the user config.");
        cmd.Subcommands.Add(BuildShow());
        cmd.Subcommands.Add(BuildPath());
        cmd.Subcommands.Add(BuildEdit());
        return cmd;
    }

    private static Command BuildShow()
    {
        var c = new Command("show", "Print the resolved config (defaults filled in).");
        c.SetAction(_ =>
        {
            var path = ConfigLoader.DefaultPath();
            var loaded = File.Exists(path)
                ? ConfigLoader.Load(path)
                : Result.Ok<UserConfig, ConfigError>(UserConfig.Default);

            switch (loaded)
            {
                case Result<UserConfig, ConfigError>.Ok ok:
                    AnsiConsole.WriteLine(TomlPrinter.Render(ok.Value));
                    return 0;
                case Result<UserConfig, ConfigError>.Err err:
                    return DiagnosticPrinter.Render(err.Error);
                default:
                    return 1;
            }
        });
        return c;
    }

    private static Command BuildPath()
    {
        var c = new Command("path", "Print the resolved config file path.");
        c.SetAction(_ =>
        {
            AnsiConsole.WriteLine(ConfigLoader.DefaultPath());
            return 0;
        });
        return c;
    }

    private static Command BuildEdit()
    {
        var c = new Command("edit", "Open the config in $EDITOR (creates default if missing).");
        c.SetAction(_ =>
        {
            var path = ConfigLoader.DefaultPath();
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, TomlPrinter.Render(UserConfig.Default));
            }

            var editor = Environment.GetEnvironmentVariable("EDITOR")
                ?? Environment.GetEnvironmentVariable("VISUAL")
                ?? "notepad.exe";

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = editor,
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
            proc?.WaitForExit();
            return 0;
        });
        return c;
    }
}
