using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Central dispatcher: holds the (immutable) sink list, the per-subsystem
/// level filter, the current <see cref="LogContext"/>, and a single
/// <see cref="ImmutableArray{T}.Empty"/> singleton for the no-fields fast
/// path (avoids ImmutableArray allocation for half of all log calls).
///
/// <para>
/// Filter lookup is a single hash hit per call — at our event volume the
/// overhead is dominated by JSON serialization in the JSONL sink, not the
/// pipeline itself.
/// </para>
/// </summary>
public sealed partial class LogPipeline(
    ImmutableArray<ILogSink> sinks,
    IReadOnlyDictionary<string, LogLevel> perSubsystem,
    LogLevel defaultLevel,
    Guid runId
)
{
    private readonly ImmutableArray<ILogSink> _sinks = sinks;
    private readonly Dictionary<string, LogLevel> _perSubsystem = new(perSubsystem, StringComparer.Ordinal);
    private readonly LogLevel _defaultLevel = defaultLevel;
    private readonly Guid _runId = runId;
    private readonly AsyncLocal<LogContext?> _context = new();

    public Guid RunId => _runId;

    public LogContext CurrentContext => _context.Value ?? new LogContext(_runId);

    public IDisposable PushContext(Func<LogContext, LogContext> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        var prev = _context.Value;
        _context.Value = mutator(CurrentContext);
        return new ContextScope(this, prev);
    }

    public bool IsEnabled(string subsystem, LogLevel level)
    {
        var threshold = _perSubsystem.TryGetValue(subsystem, out var l) ? l : _defaultLevel;
        return level >= threshold;
    }

    public void Emit(in LogEntry entry)
    {
        // _sinks is ImmutableArray — safe to iterate without lock.
        foreach (var s in _sinks)
        {
            try
            {
                s.Write(entry);
            }
            catch
            {
                // The pipeline never throws upward — a faulty sink must
                // not poison the producer. Other sinks still get the entry.
            }
        }
    }

    public void FlushAll()
    {
        foreach (var s in _sinks)
        {
            try
            {
                s.Flush();
            }
            catch
            { /* see Emit */
            }
        }
    }

    private sealed partial class ContextScope(LogPipeline owner, LogContext? prev) : IDisposable
    {
        private LogPipeline? _owner = owner;

        public void Dispose()
        {
            if (_owner is not null)
            {
                _owner._context.Value = prev;
                _owner = null;
            }
        }
    }

    /// <summary>
    /// Parse the per-subsystem filter spec from an env-var string.
    /// Format: <c>OverlayWindow=Trace,WndProc=Debug,*=Info</c>
    /// (case-insensitive level names; <c>*</c> sets the default).
    /// Unknown entries are ignored (resilient to typos).
    /// </summary>
    /// <param name="spec">Env-var value (e.g. <c>LINERULE_LOG</c>).</param>
    /// <param name="fallbackDefault">
    /// Default level to use when no <c>*=...</c> token appears in the spec
    /// (or when the spec is null/empty). Caller picks this — typically
    /// <see cref="LogLevel.Debug"/> in Debug builds, <see cref="LogLevel.Info"/>
    /// in Release.
    /// </param>
    public static (Dictionary<string, LogLevel> PerSubsystem, LogLevel Default) ParseFilterSpec(
        string? spec,
        LogLevel fallbackDefault = LogLevel.Info
    )
    {
        var perSubsystem = new Dictionary<string, LogLevel>(StringComparer.Ordinal);
        var defaultLevel = fallbackDefault;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return (perSubsystem, defaultLevel);
        }

        foreach (var token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = token.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == token.Length - 1)
            {
                continue;
            }
            var key = token[..eq].Trim();
            var levelName = token[(eq + 1)..].Trim();
            if (!Enum.TryParse<LogLevel>(levelName, ignoreCase: true, out var level))
            {
                continue;
            }
            if (string.Equals(key, "*", StringComparison.Ordinal))
            {
                defaultLevel = level;
            }
            else
            {
                perSubsystem[key] = level;
            }
        }

        return (perSubsystem, defaultLevel);
    }
}
