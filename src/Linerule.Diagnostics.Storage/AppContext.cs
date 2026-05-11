using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Linerule.Config;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Final output of <see cref="BootDag.Default"/>: every capability the
/// running overlay needs, bundled with deterministic LIFO teardown via
/// <see cref="DisposeAsync"/>. Holds the logger root, sink, crash guard
/// registration, the resolved <see cref="UserConfig"/>, and any diagnostics
/// the config loader emitted as warnings (non-fatal).
/// </summary>
public sealed class AppContext : IAsyncDisposable
{
    private readonly IDisposable? _crashGuard;
    private bool _disposed;

    public LoggerRoot Logger { get; }
    public SqliteEventSink Sink { get; }
    public UserConfig Config { get; }
    public ImmutableArray<ConfigDiagnostic> Warnings { get; }

    internal AppContext(
        LoggerRoot logger,
        SqliteEventSink sink,
        IDisposable? crashGuard,
        UserConfig config,
        ImmutableArray<ConfigDiagnostic> warnings
    )
    {
        Logger = logger;
        Sink = sink;
        _crashGuard = crashGuard;
        Config = config;
        Warnings = warnings;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // LIFO teardown: crash guard first (no longer needed), then logger
        // flush, then sink commit + WAL checkpoint (per ADR-0012).
        try
        {
            _crashGuard?.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Crash-guard teardown is best-effort: a misbehaving disposer
            // must not block sink commit + WAL checkpoint. The exception
            // filter excludes the fatal VM states per ADR-0012; everything
            // else is swallowed so the LIFO chain (logger flush + sink
            // commit) still executes.
            _ = ex;
        }
        await Logger.DisposeAsync().ConfigureAwait(false);
        Sink.Dispose();
    }
}
