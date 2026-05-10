using System.CommandLine;
using Linerule.Cli.Commands;

namespace Linerule.Cli;

internal static class CliBuilder
{
    public static RootCommand Build()
    {
        var root = new RootCommand("linerule — reading-ruler overlay for Windows");
        root.Subcommands.Add(RunCommand.Build());
        root.Subcommands.Add(ConfigCommand.Build());
        return root;
    }
}
