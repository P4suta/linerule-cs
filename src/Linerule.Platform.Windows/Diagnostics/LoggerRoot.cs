using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Linerule.Platform.Windows.Diagnostics.Sinks;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Instance-based capability that owns the diagnostic <see cref="LogPipeline"/>,
/// ring buffer, and file-sink path. Replaces the historic
/// <c>static Logger</c> facade — composition root constructs one
/// <see cref="LoggerRoot"/> via <see cref="Create"/> and threads it through
/// constructors, so tests can swap in an isolated handle without poking at
/// process globals.
///
/// <para>
/// The shape mirrors the static <c>Logger</c> for one release so call sites
/// migrate one PR at a time; later, the static façade goes away.
/// </para>
/// </summary>
public sealed class LoggerRoot : IAsyncDisposable
{
    private readonly RingBufferSink _ringBuffer;
    private readonly string? _dbPath;
    private bool _disposed;

    internal LoggerRoot(LogPipeline pipeline, RingBufferSink ringBuffer, string? dbPath)
    {
        Pipeline = pipeline;
        _ringBuffer = ringBuffer;
        _dbPath = dbPath;
    }

    /// <summary>Subsystem-bound handle. Cheap to call; just a record copy.</summary>
    public LoggerHandle For(string subsystem)
    {
        ArgumentNullException.ThrowIfNull(subsystem);
        return new LoggerHandle(Pipeline, subsystem);
    }

    /// <summary>Process-wide RunId; stamps every entry's <c>ctx.run_id</c>.</summary>
    public Guid RunId => Pipeline.RunId;

    /// <summary>Persistent event-store path (empty when running with no file sink).</summary>
    public string DbPath => _dbPath ?? string.Empty;

    /// <summary>Snapshot of the last N entries (oldest first), for crash dumps.</summary>
    public IReadOnlyList<LogEntry> RecentEntries() => _ringBuffer.Snapshot();

    /// <summary>Configured ring-buffer capacity (NOT current entry count).</summary>
    public int RingCapacity => _ringBuffer.Capacity;

    public IDisposable PushSession(Guid sessionId) => Pipeline.PushContext(c => c with { SessionId = sessionId });

    public IDisposable PushFrame(long frameSeq) => Pipeline.PushContext(c => c with { FrameSeq = frameSeq });

    public IDisposable PushActivity(string activityId)
    {
        ArgumentNullException.ThrowIfNull(activityId);
        return Pipeline.PushContext(c => c with { ActivityId = activityId });
    }

    /// <summary>Internal accessor for <see cref="CrashDump"/>.</summary>
    internal LogPipeline Pipeline { get; }

    /// <summary>
    /// Build a fresh <see cref="LoggerRoot"/>. Mirrors <c>Logger.Initialize</c>:
    /// stdout + ring-buffer sinks unconditionally, optional persistent
    /// <paramref name="fileSink"/> (typically <c>SqliteEventSink</c>), and
    /// <c>LINERULE_LOG=*=…</c> env-driven filter spec.
    /// </summary>
    public static LoggerRoot Create(int ringCapacity = 200, LogLevel? defaultLevel = null, ILogSink? fileSink = null)
    {
        var spec = Environment.GetEnvironmentVariable("LINERULE_LOG");
        var fallback = defaultLevel ?? BuildConfigDefaultLevel();
        var (perSubsystem, effectiveDefault) = LogPipeline.ParseFilterSpec(spec, fallback);

        var ring = new RingBufferSink(ringCapacity);
        var stdout = new StdoutSink();
        ImmutableArray<ILogSink> sinks = fileSink is null ? [stdout, ring] : [stdout, fileSink, ring];
        var pipeline = new LogPipeline(sinks, perSubsystem, effectiveDefault, Guid.NewGuid());
        return new LoggerRoot(pipeline, ring, ExtractPath(fileSink));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        try
        {
            Pipeline.FlushAll();
        }
        catch
        {
            // Mirror LogPipeline.Emit: never propagate from logger internals.
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Best-effort discovery of a sink's on-disk path (e.g. SqliteEventSink.Path).
    /// Reflection is acceptable here: bootstrap-once, not a hot path.
    /// </summary>
    private static string? ExtractPath(ILogSink? sink)
    {
        if (sink is null)
        {
            return null;
        }
        var prop = sink.GetType().GetProperty("Path");
        return prop?.GetValue(sink) as string;
    }

    private static LogLevel BuildConfigDefaultLevel() =>
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif
}
