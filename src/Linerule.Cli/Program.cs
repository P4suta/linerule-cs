using System.CommandLine;
using Linerule.Bootstrap;
using Linerule.Cli;
using Linerule.Cli.Diagnostics;
using Linerule.Core;
using Linerule.Diagnostics;
using AppContext = Linerule.Diagnostics.Storage.AppContext;
using BootDag = Linerule.Diagnostics.Storage.BootDag;

// Boot DAG (typed Kleisli composition): OpenSqlite >=> InitLogger >=>
// InstallCrash >=> LoadConfig >=> AssembleContext.
// Capability tokens enforce ordering at the C# type level — re-shuffling
// the phases is a compile error, not a runtime drift.
var boot = await BootDag.Default().Run(BootArgs.FromArgv(args), default).ConfigureAwait(false);
if (boot is Result<AppContext, BootstrapError>.Err bootErr)
{
    return DiagnosticPrinter.Render(LineruleError.FromBootstrap(bootErr.Error));
}
await using var ctx = ((Result<AppContext, BootstrapError>.Ok)boot).Value;
return await CliBuilder.Build(ctx).Parse(args).InvokeAsync().ConfigureAwait(false);
