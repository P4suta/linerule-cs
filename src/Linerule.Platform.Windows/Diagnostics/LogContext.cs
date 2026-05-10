namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Correlation context attached to every <see cref="LogEntry"/>. Flows
/// through <see cref="System.Threading.AsyncLocal{T}"/> so nested awaits
/// inherit the parent's IDs without explicit threading.
///
/// <para>
/// <see cref="RunId"/> is one per process (set when <see cref="Logger"/>
/// initializes). <see cref="SessionId"/>, <see cref="FrameSeq"/>, and
/// <see cref="ActivityId"/> are scoped — set/cleared via
/// <c>Logger.PushSession</c> / <c>Logger.PushActivity</c>.
/// </para>
/// </summary>
public readonly record struct LogContext(
    System.Guid RunId,
    System.Guid? SessionId = null,
    long? FrameSeq = null,
    string? ActivityId = null);
