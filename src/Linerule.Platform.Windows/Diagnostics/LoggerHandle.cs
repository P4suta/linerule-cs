using System;
using System.Collections.Immutable;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Subsystem-bound log handle. Constructors take this and never the static
/// <see cref="Logger"/> facade — DI-friendly without DI infrastructure.
///
/// <para>
/// Overloads up to 4 fields are explicit (avoid <c>params</c> array
/// allocation on the hot path). The <c>params</c> overload is the catchall
/// for ≥5 fields.
/// </para>
/// </summary>
public sealed class LoggerHandle
{
    private static readonly ImmutableArray<LogField> EmptyFields = ImmutableArray<LogField>.Empty;

    private readonly LogPipeline _pipeline;
    public string Subsystem { get; }

    internal LoggerHandle(LogPipeline pipeline, string subsystem)
    {
        _pipeline = pipeline;
        Subsystem = subsystem;
    }

    public bool IsEnabled(LogLevel level) => _pipeline.IsEnabled(Subsystem, level);

    // ----- arity-specialized overloads (alloc-free for ≤4 fields) -----

    public void Trace(string step) => Log(LogLevel.Trace, step, EmptyFields, null);
    public void Trace(string step, LogField f1) => Log(LogLevel.Trace, step, ImmutableArray.Create(f1), null);
    public void Trace(string step, LogField f1, LogField f2) => Log(LogLevel.Trace, step, ImmutableArray.Create(f1, f2), null);
    public void Trace(string step, LogField f1, LogField f2, LogField f3) => Log(LogLevel.Trace, step, ImmutableArray.Create(f1, f2, f3), null);
    public void Trace(string step, LogField f1, LogField f2, LogField f3, LogField f4) => Log(LogLevel.Trace, step, ImmutableArray.Create(f1, f2, f3, f4), null);
    public void Trace(string step, params LogField[] fields) => Log(LogLevel.Trace, step, fields.ToImmutableArray(), null);

    public void Debug(string step) => Log(LogLevel.Debug, step, EmptyFields, null);
    public void Debug(string step, LogField f1) => Log(LogLevel.Debug, step, ImmutableArray.Create(f1), null);
    public void Debug(string step, LogField f1, LogField f2) => Log(LogLevel.Debug, step, ImmutableArray.Create(f1, f2), null);
    public void Debug(string step, LogField f1, LogField f2, LogField f3) => Log(LogLevel.Debug, step, ImmutableArray.Create(f1, f2, f3), null);
    public void Debug(string step, LogField f1, LogField f2, LogField f3, LogField f4) => Log(LogLevel.Debug, step, ImmutableArray.Create(f1, f2, f3, f4), null);
    public void Debug(string step, params LogField[] fields) => Log(LogLevel.Debug, step, fields.ToImmutableArray(), null);

    public void Info(string step) => Log(LogLevel.Info, step, EmptyFields, null);
    public void Info(string step, LogField f1) => Log(LogLevel.Info, step, ImmutableArray.Create(f1), null);
    public void Info(string step, LogField f1, LogField f2) => Log(LogLevel.Info, step, ImmutableArray.Create(f1, f2), null);
    public void Info(string step, LogField f1, LogField f2, LogField f3) => Log(LogLevel.Info, step, ImmutableArray.Create(f1, f2, f3), null);
    public void Info(string step, LogField f1, LogField f2, LogField f3, LogField f4) => Log(LogLevel.Info, step, ImmutableArray.Create(f1, f2, f3, f4), null);
    public void Info(string step, params LogField[] fields) => Log(LogLevel.Info, step, fields.ToImmutableArray(), null);

    public void Warn(string step) => Log(LogLevel.Warn, step, EmptyFields, null);
    public void Warn(string step, LogField f1) => Log(LogLevel.Warn, step, ImmutableArray.Create(f1), null);
    public void Warn(string step, LogField f1, LogField f2) => Log(LogLevel.Warn, step, ImmutableArray.Create(f1, f2), null);
    public void Warn(string step, LogField f1, LogField f2, LogField f3) => Log(LogLevel.Warn, step, ImmutableArray.Create(f1, f2, f3), null);
    public void Warn(string step, LogField f1, LogField f2, LogField f3, LogField f4) => Log(LogLevel.Warn, step, ImmutableArray.Create(f1, f2, f3, f4), null);
    public void Warn(string step, params LogField[] fields) => Log(LogLevel.Warn, step, fields.ToImmutableArray(), null);

    public void Error(string step, Exception? ex = null) => Log(LogLevel.Error, step, EmptyFields, ex);
    public void Error(string step, Exception? ex, LogField f1) => Log(LogLevel.Error, step, ImmutableArray.Create(f1), ex);
    public void Error(string step, Exception? ex, LogField f1, LogField f2) => Log(LogLevel.Error, step, ImmutableArray.Create(f1, f2), ex);
    public void Error(string step, Exception? ex, params LogField[] fields) => Log(LogLevel.Error, step, fields.ToImmutableArray(), ex);

    private void Log(LogLevel level, string step, ImmutableArray<LogField> fields, Exception? ex)
    {
        if (!_pipeline.IsEnabled(Subsystem, level))
        {
            return;
        }
        var entry = new LogEntry(
            Timestamp: DateTimeOffset.UtcNow,
            Level: level,
            Subsystem: Subsystem,
            Step: step,
            Context: _pipeline.CurrentContext,
            Fields: fields,
            Exception: ex);
        _pipeline.Emit(entry);
    }
}
