namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Effectful side of the log pipeline. A sink consumes
/// <see cref="LogEntry"/> values; behavior on exception is sink-specific
/// but must NOT propagate (the producer is logging because something
/// already deserves attention — the logger itself must not become a
/// new failure surface).
/// </summary>
public interface ILogSink
{
    void Write(in LogEntry entry);
    void Flush();
}
