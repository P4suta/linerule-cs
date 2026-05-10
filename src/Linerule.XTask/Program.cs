using System.CommandLine;
using Linerule.XTask;

var root = new RootCommand("linerule xtask — repo-level defensive gates");
root.Subcommands.Add(StrictCodeCommand.Build());
return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
