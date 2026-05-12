using System;
using System.Threading.Tasks;
using Linerule.Bootstrap;
using Linerule.Core;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// The canonical startup composition: <code>OpenSqlite ≫ InitLogger ≫ InstallCrash
/// ≫ Assemble</code>. Phases are <see cref="Phase{TIn, TOut}"/> Kleisli arrows
/// over <see cref="Result{T, BootstrapError}"/>; each phase declares its
/// required input as a typed capability token, so swapping the order is a
/// CS1503 compile error.
///
/// <para>
/// ADR-0015: <see cref="UserConfig.Default"/> is the single source of truth
/// for tunables. The historic <c>LoadConfig</c> phase (TOML file → Tomlyn
/// reflection → validated record) is gone; <see cref="AssembleContext"/>
/// injects the constant directly.
/// </para>
///
/// <para>
/// Both <c>Linerule.Cli.Program</c> and <c>Linerule.App.Program</c> reuse
/// <see cref="Default"/>; the only diverging code is the post-context
/// "what to do with the running overlay" step.
/// </para>
/// </summary>
public static partial class BootDag
{
    /// <summary>
    /// Construct the canonical boot DAG.
    /// </summary>
    public static Phase<BootArgs, AppContext> Default() =>
        OpenSqlite().Then(InitLogger()).Then(InstallCrash()).Then(AssembleContext());

    public static Phase<BootArgs, SqliteSeed> OpenSqlite() =>
        Phase.FromResult<BootArgs, SqliteSeed>(
            "open-sqlite",
            args =>
            {
                try
                {
                    var path = SqlitePath.DefaultPath();
                    var argv = ToArray(args.Argv);
                    var sink = new SqliteEventSink(path, RunMetadata.Capture(argv));
                    return Result.Ok<SqliteSeed, BootstrapError>(new SqliteSeed(sink, path));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return Result.Err<SqliteSeed, BootstrapError>(new BootstrapError.Threw("open-sqlite", ex));
                }
            }
        );

    public static Phase<SqliteSeed, LoggerSeed> InitLogger() =>
        Phase.FromResult<SqliteSeed, LoggerSeed>(
            "init-logger",
            seed =>
            {
                try
                {
                    // Construct the diagnostic root as a typed capability that
                    // gets threaded through every consumer's ctor. No process-wide
                    // static is involved — tests build their own LoggerRoot and
                    // pass it to the same phases.
                    var root = LoggerRoot.Create(fileSink: seed.Sink);
                    return Result.Ok<LoggerSeed, BootstrapError>(new LoggerSeed(seed, root));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return Result.Err<LoggerSeed, BootstrapError>(new BootstrapError.Threw("init-logger", ex));
                }
            }
        );

    public static Phase<LoggerSeed, CrashSeed> InstallCrash() =>
        Phase.FromResult<LoggerSeed, CrashSeed>(
            "install-crash",
            seed =>
            {
                try
                {
                    // CrashDump.Install captures the LoggerRoot so the unhandled
                    // exception hook can emit RecentEntries / RunId from the
                    // same pipeline the running app uses. The hook itself is
                    // process-wide (AppDomain.UnhandledException is a single
                    // multicast delegate) — Registration is currently a noop
                    // disposable; FOLLOWUP: dispose to detach + reset _root.
                    CrashDump.Install(seed.Logger);
                    IDisposable registration = NoopDisposable.Instance;
                    return Result.Ok<CrashSeed, BootstrapError>(new CrashSeed(seed, registration));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return Result.Err<CrashSeed, BootstrapError>(new BootstrapError.Threw("install-crash", ex));
                }
            }
        );

    public static Phase<CrashSeed, AppContext> AssembleContext() =>
        Phase.Pure<CrashSeed, AppContext>(
            "assemble",
            seed => new AppContext(
                logger: seed.Logger.Logger,
                sink: seed.Logger.Sqlite.Sink,
                crashGuard: seed.Registration,
                config: UserConfig.Default
            )
        );

    private static string[] ToArray(System.Collections.Generic.IReadOnlyList<string> source)
    {
        var arr = new string[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            arr[i] = source[i];
        }
        return arr;
    }

    private sealed partial class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}
