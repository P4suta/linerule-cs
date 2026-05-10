using System.Threading;

namespace Linerule.Platform.Windows.Diagnostics.Sinks;

/// <summary>
/// Lock-free-ish circular buffer that retains the last <c>capacity</c>
/// entries. Read by <see cref="CrashDump"/> when an unhandled exception
/// fires — the dump contains the breadcrumb trail leading to the failure
/// without needing to keep the entire process log in memory.
/// </summary>
internal sealed class RingBufferSink : ILogSink
{
    private readonly Lock _gate = new();
    private readonly LogEntry[] _buffer;
    private int _head;
    private int _count;

    public RingBufferSink(int capacity)
    {
        System.ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _buffer = new LogEntry[capacity];
    }

    public int Capacity => _buffer.Length;

    public void Write(in LogEntry entry)
    {
        lock (_gate)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    public void Flush() { /* no-op */ }

    /// <summary>
    /// Snapshot of the buffer in chronological order (oldest first).
    /// </summary>
    public LogEntry[] Snapshot()
    {
        lock (_gate)
        {
            var result = new LogEntry[_count];
            var start = (_head - _count + _buffer.Length) % _buffer.Length;
            for (var i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _buffer.Length];
            }
            return result;
        }
    }
}
