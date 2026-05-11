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
        cmd.Subcommands.Add(BuildPrintDefault());
        cmd.Subcommands.Add(BuildValidate());
        cmd.Subcommands.Add(BuildSign());
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

            var editor =
                Environment.GetEnvironmentVariable("EDITOR")
                ?? Environment.GetEnvironmentVariable("VISUAL")
                ?? "notepad.exe";

            using var proc = Process.Start(
                new ProcessStartInfo
                {
                    FileName = editor,
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                }
            );
            proc?.WaitForExit();
            return 0;
        });
        return c;
    }

    private static Command BuildPrintDefault()
    {
        var c = new Command("print-default", "Print the documented default config to stdout (TOML).");
        c.SetAction(_ =>
        {
            Console.Out.Write(TomlPrinter.Render(UserConfig.Default));
            return 0;
        });
        return c;
    }

    private static Command BuildValidate()
    {
        var c = new Command("validate", "Load + validate the config; exit non-zero on any error.");
        var pathArg = new Argument<string?>("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null,
            Description = "Optional config path (default: resolved default path).",
        };
        c.Arguments.Add(pathArg);
        c.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArg) ?? ConfigLoader.DefaultPath();
            if (!File.Exists(path))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]error:[/] config not found at [yellow]{path}[/]");
                return 2;
            }
            var loaded = ConfigLoader.Load(path);
            switch (loaded)
            {
                case Result<UserConfig, ConfigError>.Ok ok:
                    AnsiConsole.MarkupLineInterpolated($"[green]OK[/] {path}");
                    _ = ok;
                    return 0;
                case Result<UserConfig, ConfigError>.Err err:
                    return DiagnosticPrinter.Render(err.Error);
                default:
                    return 1;
            }
        });
        return c;
    }

    private static Command BuildSign()
    {
        var c = new Command("sign", "Compute SHA-256 of the config and write a .sha256 sidecar.");
        var pathArg = new Argument<string?>("path")
        {
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => null,
            Description = "Optional config path (default: resolved default path).",
        };
        c.Arguments.Add(pathArg);
        c.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathArg) ?? ConfigLoader.DefaultPath();
            if (!File.Exists(path))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]error:[/] config not found at [yellow]{path}[/]");
                return 2;
            }
            try
            {
                var text = File.ReadAllText(path);
                var hash = FileIntegrity.ComputeSha256Hex(text);
                File.WriteAllText(path + FileIntegrity.SidecarExtension, hash + "\n");
                AnsiConsole.MarkupLineInterpolated($"[green]signed[/] {path}{FileIntegrity.SidecarExtension}");
                AnsiConsole.WriteLine(hash);
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {ex.Message}");
                return 1;
            }
        });
        return c;
    }
}
