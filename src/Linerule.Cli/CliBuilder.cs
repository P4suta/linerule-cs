using System.CommandLine;
using Linerule.Cli.Commands;
using AppContext = Linerule.Diagnostics.Storage.AppContext;

namespace Linerule.Cli;

internal static class CliBuilder
{
    public static RootCommand Build(AppContext ctx)
    {
        var root = new RootCommand("linerule — reading-ruler overlay for Windows");
        root.Subcommands.Add(RunCommand.Build(ctx));
        // Argless invocation = run. The run subcommand's description
        // ("Start the overlay (default).") promises this; this line keeps the
        // promise. The GUI launcher (Linerule.App) relies on the same default
        // shape — both frontends collapse to RunCommand.Execute(ctx, ct).
        root.SetAction((_, ct) => RunCommand.Execute(ctx, ct));
        return root;
    }
}
