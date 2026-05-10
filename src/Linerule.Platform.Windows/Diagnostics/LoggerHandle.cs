using System;
using System.Collections.Immutable;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Subsystem-bound log handle. Constructors take this and never the static
/// <see cref="Logger"/> facade — DI-friendly without DI infrastructure.
///
/// <para>
/// All level-typed methods take <c>params LogField[]</c>. The empty-array
/// case allocates once and is cached as <see cref="EmptyFields"/>; non-empty
/// calls allocate one small array. Hot paths (e.g. WndProc) should still
/// gate with <see cref="IsEnabled"/> at the call site to skip even the
/// field-value computations when the level is filtered out.
/// </para>
///
/// <para>
/// We tried <c>params ReadOnlySpan&lt;LogField&gt;</c> (C# 13) but the
/// combination with target-typed <c>new(...)</c> at call sites broke
/// overload resolution into a confusing
/// "cannot convert string to void*" — the array form is unambiguous and
/// our event volume (≤ a few thousand per second worst case) makes the
/// extra allocation immeasurable.
/// </para>
/// </summary>
public sealed class LoggerHandle
{
    private static readonly TimeProvider Time = TimeProvider.System;

    private readonly LogPipeline _pipeline;
    public string Subsystem { get; }

    internal LoggerHandle(LogPipeline pipeline, string subsystem)
    {
        _pipeline = pipeline;
        Subsystem = subsystem;
    }

    public bool IsEnabled(LogLevel level) => _pipeline.IsEnabled(Subsystem, level);

    public void Trace(string step, params LogField[]? fields) => Emit(LogLevel.Trace, step, fields, ex: null);

    public void Debug(string step, params LogField[]? fields) => Emit(LogLevel.Debug, step, fields, ex: null);

    public void Info(string step, params LogField[]? fields) => Emit(LogLevel.Info, step, fields, ex: null);

    public void Warn(string step, params LogField[]? fields) => Emit(LogLevel.Warn, step, fields, ex: null);

    public void Error(string step, Exception? ex = null, params LogField[]? fields) =>
        Emit(LogLevel.Error, step, fields, ex);

    private void Emit(LogLevel level, string step, LogField[]? fields, Exception? ex)
    {
        if (!_pipeline.IsEnabled(Subsystem, level))
        {
            return;
        }
        var snapshot = fields is null || fields.Length == 0 ? [] : ImmutableArray.Create(fields);
        var entry = new LogEntry(
            Timestamp: Time.GetUtcNow(),
            Level: level,
            Subsystem: Subsystem,
            Step: step,
            Context: _pipeline.CurrentContext,
            Fields: snapshot,
            Exception: ex
        );
        _pipeline.Emit(entry);
    }
}
