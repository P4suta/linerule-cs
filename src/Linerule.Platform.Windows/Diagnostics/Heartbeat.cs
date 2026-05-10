using System;
using System.Threading;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Periodic "alive" pulse. Emits a TRACE-level entry every
/// <see cref="Interval"/> via the <see cref="Subsystems.Heartbeat"/>
/// channel, with the current snapshot from a caller-supplied
/// <see cref="Func{T}"/>. A gap in heartbeat lines in the JSONL log is
/// the canonical signal of a wedge / freeze.
///
/// <para>
/// The default <see cref="Subsystems.Heartbeat"/> is at TRACE level, so
/// it is silent by default; flip <c>LINERULE_LOG=Heartbeat=Trace</c>
/// (or anywhere in the spec) to surface it.
/// </para>
/// </summary>
public sealed class Heartbeat : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    private readonly Timer _timer;
    private readonly LoggerHandle _log;
    private readonly Func<LogField[]> _snapshot;
    private long _seq;
    private int _disposed;

    public TimeSpan Interval { get; }

    public Heartbeat(Func<LogField[]> snapshot, TimeSpan? interval = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _log = Logger.For(Subsystems.Heartbeat);
        Interval = interval ?? DefaultInterval;
        _timer = new Timer(_ => Tick(), null, Interval, Interval);
    }

    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }
        try
        {
            var seq = Interlocked.Increment(ref _seq);
            var fields = _snapshot();
            if (fields is null || fields.Length == 0)
            {
                _log.Trace("alive", new("seq", seq));
                return;
            }
            var combined = new LogField[fields.Length + 1];
            combined[0] = new("seq", seq);
            Array.Copy(fields, 0, combined, 1, fields.Length);
            _log.Trace("alive", combined);
        }
        catch (Exception ex)
        {
            _log.Warn("snapshot threw", new("type", ex.GetType().FullName ?? "?"), new("msg", ex.Message));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _timer.Dispose();
    }
}
