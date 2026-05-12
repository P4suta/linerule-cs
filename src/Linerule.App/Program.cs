using Linerule.Bootstrap;
using Linerule.Core;
using Linerule.Platform.Windows;
using AppContext = Linerule.Diagnostics.Storage.AppContext;
using BootDag = Linerule.Diagnostics.Storage.BootDag;

// WinExe entry. Boot DAG drives every startup step; if it fails we still
// produce a SQLite-logged crash trace (ADR-0012) — the WinExe has no
// console attached so a Spectre render would land nowhere visible.
// ADR-0015: tunables are compile-time constants — no config file loaded.
var boot = await BootDag.Default().Run(BootArgs.FromArgv(args), default).ConfigureAwait(false);
if (boot is Result<AppContext, BootstrapError>.Err)
{
    // Bootstrap couldn't even open the event store; nothing actionable
    // from a WinExe perspective beyond the exit code.
    return 2;
}

// Hold the AppContext separately so the `await using` block keeps the
// original IAsyncDisposable in scope. `ConfigureAwait(false)` on the
// disposable routes the implicit DisposeAsync continuation off the
// SynchronizationContext (CA2007 / MA0004 / xUnit1030 patterns).
var ctx = ((Result<AppContext, BootstrapError>.Ok)boot).Value;
await using var ctxScope = ctx.ConfigureAwait(false);
return await WindowsApp.RunAsync(ctx.Config, ctx.Logger, System.Threading.CancellationToken.None).ConfigureAwait(false);
