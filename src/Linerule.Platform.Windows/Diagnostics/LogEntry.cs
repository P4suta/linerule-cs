using System;
using System.Collections.Immutable;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Immutable log record. Sinks consume entries; producers are pure value-builders.
/// <para>
/// <see cref="Fields"/> is an <see cref="ImmutableArray{T}"/> not a dictionary
/// because (a) call sites usually have ≤4 fields, (b) ordering is preserved
/// for human-readable rendering, and (c) duplicate keys are explicit (sink
/// decides serialization strategy).
/// </para>
/// </summary>
public readonly record struct LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Subsystem,
    string Step,
    LogContext Context,
    ImmutableArray<LogField> Fields,
    Exception? Exception = null);
