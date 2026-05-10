using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using Linerule.Platform.Windows.Diagnostics.Sinks;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Static facade over the diagnostic <see cref="LogPipeline"/>. Owns the
/// process-wide singletons (pipeline, ring buffer, file path, RunId) and
/// hands out subsystem-bound <see cref="LoggerHandle"/>s via
/// <see cref="For"/>.
///
/// <para>
/// <b>Bootstrap protocol</b> (must be the first call from
/// <c>Linerule.Cli.Program</c>):
/// </para>
///
/// <code>
/// Logger.Initialize();             // create pipeline + sinks
/// CrashDump.Install();             // wire UnhandledException → dump
/// // ... actual work ...
/// Logger.Shutdown();               // flush + dispose sinks
/// </code>
///
/// <para>
/// Calling any logging method before <see cref="Initialize"/> is a
/// programming error and throws — silent no-op would hide the bootstrap
/// bug we are explicitly designing observability infrastructure to surface.
/// </para>
/// </summary>
public static class Logger
{
    private static LogPipeline? _pipeline;
    private static RingBufferSink? _ringBuffer;
    private static JsonlFileSink? _jsonlSink;
    private static string? _logPath;
    private static int _initialized;

    /// <summary>
    /// Set up the global pipeline. Idempotent — second call returns the
    /// existing pipeline (does not re-initialize).
    /// </summary>
    /// <param name="logPath">
    /// Override the JSONL output path. Default:
    /// <c>%TEMP%\linerule.jsonl</c>.
    /// </param>
    /// <param name="ringCapacity">
    /// Ring-buffer depth for crash-dump replay. Default 200.
    /// </param>
    /// <param name="defaultLevel">
    /// Override the fallback level used when neither
    /// <c>LINERULE_LOG=*=...</c> nor a per-subsystem rule applies.
    /// Default: <see cref="LogLevel.Debug"/> in Debug builds,
    /// <see cref="LogLevel.Info"/> in Release.
    /// </param>
    public static void Initialize(string? logPath = null, int ringCapacity = 200, LogLevel? defaultLevel = null)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        var path = logPath ?? Path.Combine(Path.GetTempPath(), "linerule.jsonl");
        var spec = Environment.GetEnvironmentVariable("LINERULE_LOG");
        var append = ParseBoolEnv("LINERULE_LOG_APPEND", defaultValue: false);
        var fallback = defaultLevel ?? BuildConfigDefaultLevel();
        var (perSubsystem, effectiveDefault) = LogPipeline.ParseFilterSpec(spec, fallback);

        var ring = new RingBufferSink(ringCapacity);
        var jsonl = new JsonlFileSink(path, append);
        var stdout = new StdoutSink();

        var sinks = ImmutableArray.Create<ILogSink>(stdout, jsonl, ring);
        var pipeline = new LogPipeline(sinks, perSubsystem, effectiveDefault, Guid.NewGuid());

        _pipeline = pipeline;
        _ringBuffer = ring;
        _jsonlSink = jsonl;
        _logPath = path;

        AppDomain.CurrentDomain.ProcessExit += static (_, _) => Shutdown();

        var bootstrap = For(Subsystems.Logger);
        bootstrap.Info(
            "initialized",
            new LogField("run_id", pipeline.RunId.ToString("D")),
            new LogField("jsonl", path),
            new LogField("jsonl_mode", append ? "append" : "truncate"),
            new LogField("default_level", effectiveDefault),
            new LogField("build_config", BuildConfigName()),
            new LogField("subsystem_overrides", spec ?? string.Empty)
        );
    }

    /// <summary>
    /// Parse a boolean env var. Recognizes <c>1 / true / yes / on</c> as
    /// true, anything else (including unset) as <paramref name="defaultValue"/>.
    /// </summary>
    private static bool ParseBoolEnv(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }
        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => defaultValue,
        };
    }

    /// <summary>
    /// Tear down: flush all sinks, dispose the JSONL file. Idempotent.
    /// Called automatically on <see cref="AppDomain.ProcessExit"/>.
    /// </summary>
    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _initialized, 0) == 0)
        {
            return;
        }
        try
        {
            _pipeline?.FlushAll();
        }
        catch
        {
            // see LogPipeline.Emit — never propagate from logger internals
        }
        try
        {
            _jsonlSink?.Dispose();
        }
        catch
        {
            // ditto
        }
        _pipeline = null;
        _ringBuffer = null;
        _jsonlSink = null;
        _logPath = null;
    }

    /// <summary>
    /// Subsystem-bound handle. Consumers should hold one as a
    /// <c>private static readonly</c> field (cheap; just an interior
    /// reference + a string).
    /// </summary>
    public static LoggerHandle For(string subsystem)
    {
        var p =
            _pipeline
            ?? throw new InvalidOperationException(
                "Logger.For called before Logger.Initialize. The bootstrap order is: Logger.Initialize → CrashDump.Install → ... actual work."
            );
        return new LoggerHandle(p, subsystem);
    }

    /// <summary>JSONL file path. Surfaces in startup banner so the user can find it.</summary>
    public static string LogPath => _logPath ?? string.Empty;

    /// <summary>Process-wide RunId. Stamps every entry's <c>ctx.run_id</c>.</summary>
    public static Guid RunId => _pipeline?.RunId ?? Guid.Empty;

    /// <summary>Snapshot of the last N entries (oldest first), for crash dumps.</summary>
    public static IReadOnlyList<LogEntry> RecentEntries() => _ringBuffer?.Snapshot() ?? [];

    /// <summary>
    /// Configured capacity of the crash-dump ring buffer (NOT the
    /// current entry count). 0 if the logger isn't initialized.
    /// </summary>
    public static int RingCapacity => _ringBuffer?.Capacity ?? 0;

    /// <summary>
    /// Push a session-scoped context (e.g. one overlay session). Returns
    /// an <see cref="IDisposable"/> that pops the scope when disposed —
    /// nest with <see langword="using"/>.
    /// </summary>
    public static IDisposable PushSession(Guid sessionId) =>
        (_pipeline ?? throw NotInitialized()).PushContext(c => c with { SessionId = sessionId });

    /// <summary>Push a frame-seq scope (per repaint).</summary>
    public static IDisposable PushFrame(long frameSeq) =>
        (_pipeline ?? throw NotInitialized()).PushContext(c => c with { FrameSeq = frameSeq });

    /// <summary>Push an activity-id scope (per logical operation).</summary>
    public static IDisposable PushActivity(string activityId) =>
        (_pipeline ?? throw NotInitialized()).PushContext(c => c with { ActivityId = activityId });

    /// <summary>Internal helper — exposes the pipeline to <see cref="CrashDump"/>.</summary>
    internal static LogPipeline? Pipeline => _pipeline;

    /// <summary>
    /// Build-config-aware default level. <c>DEBUG</c> compilation symbol
    /// (set by <c>dotnet build -c Debug</c>) means the developer wants
    /// noise — we default to <see cref="LogLevel.Debug"/>. Release builds
    /// default to <see cref="LogLevel.Info"/>. Either is overridden by
    /// the <c>LINERULE_LOG=*=...</c> env var.
    /// </summary>
    private static LogLevel BuildConfigDefaultLevel() =>
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    /// <summary>
    /// Symbolic build-config tag for the bootstrap log line — surfaces
    /// "is this binary the optimized one or the debuggable one" without
    /// reading PE flags.
    /// </summary>
    private static string BuildConfigName() =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static InvalidOperationException NotInitialized() => new("Logger.PushXxx called before Logger.Initialize.");
}
