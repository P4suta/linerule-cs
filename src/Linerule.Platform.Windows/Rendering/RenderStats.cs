using System;
using System.Threading;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Rolling-window frame-time statistics. <see cref="Record"/> is called
/// once per tick from the render thread; <see cref="Snapshot"/> is read
/// periodically (e.g. by <see cref="Heartbeat"/>) for telemetry.
///
/// <para>
/// Stores raw <see cref="TimeSpan.Ticks"/> in a circular buffer; on
/// snapshot, copies the live samples into a sortable array, sorts once,
/// and reads p50 / p95 / p99 from index. O(N log N) per snapshot
/// (N = capacity, default 240 frames ≈ 2 s at 120 Hz) is well within budget
/// even at 1 Hz heartbeat.
/// </para>
///
/// <para>
/// All access is gated by a single <see cref="System.Threading.Lock"/>:
/// <see cref="Record"/> is on the render thread; <see cref="Snapshot"/>
/// can come from any thread. The hot path (<see cref="Record"/>) takes
/// the lock for one assignment + counter bump, so contention is negligible.
/// </para>
/// </summary>
public sealed class RenderStats
{
    private const int DefaultCapacity = 240;

    private readonly Lock _gate = new();
    private readonly long[] _samples;
    private int _head;
    private int _count;
    private long _totalFrames;
    private long _droppedFrames;
    private long _commitTimeouts;

    public int Capacity => _samples.Length;
    public long TotalFrames => Interlocked.Read(ref _totalFrames);
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    /// <summary>
    /// Total number of times the render loop's commit-await timeout fired
    /// (compositor stalled — RDP / locked session / minimized window).
    /// Steady-state on a normal desktop should stay at 0.
    /// </summary>
    public long CommitTimeouts => Interlocked.Read(ref _commitTimeouts);

    public RenderStats(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new long[capacity];
    }

    /// <summary>Record one frame's elapsed time.</summary>
    public void Record(TimeSpan frameTime)
    {
        Interlocked.Increment(ref _totalFrames);
        lock (_gate)
        {
            _samples[_head] = frameTime.Ticks;
            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>Mark one frame as having exceeded its budget.</summary>
    public void RecordDrop() => Interlocked.Increment(ref _droppedFrames);

    /// <summary>
    /// Mark one commit-await timeout (compositor failed to fire within the
    /// configured ceiling — see <see cref="RenderClock"/>).
    /// </summary>
    public void RecordCommitTimeout() => Interlocked.Increment(ref _commitTimeouts);

    /// <summary>
    /// Snapshot the rolling window. Returns the empty snapshot if no
    /// samples have been recorded yet (avoids "what is the p99 of nothing"
    /// undefined behavior at startup).
    /// </summary>
    public RenderStatsSnapshot Snapshot()
    {
        long[] copy;
        int n;
        lock (_gate)
        {
            n = _count;
            if (n == 0)
            {
                // No frame samples yet — but RecordDrop() / RecordCommitTimeout()
                // may still have accrued. Return a snapshot with zero timing
                // values plus the live counters so they aren't lost.
                return RenderStatsSnapshot.Empty with
                {
                    TotalFrames = TotalFrames,
                    DroppedFrames = DroppedFrames,
                    CommitTimeouts = CommitTimeouts,
                };
            }
            copy = new long[n];
            // Linearize the ring into ascending-time order for percentile
            // computation. Order doesn't affect percentiles but it makes
            // future "last N frames" reads cheaper.
            var start = (_head - n + _samples.Length) % _samples.Length;
            for (var i = 0; i < n; i++)
            {
                copy[i] = _samples[(start + i) % _samples.Length];
            }
        }
        Array.Sort(copy);
        long min = copy[0];
        long max = copy[n - 1];
        long sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += copy[i];
        }
        return new RenderStatsSnapshot(
            SampleCount: n,
            TotalFrames: TotalFrames,
            DroppedFrames: DroppedFrames,
            CommitTimeouts: CommitTimeouts,
            Min: TimeSpan.FromTicks(min),
            Mean: TimeSpan.FromTicks(sum / n),
            P50: TimeSpan.FromTicks(copy[Index(n, 0.50)]),
            P95: TimeSpan.FromTicks(copy[Index(n, 0.95)]),
            P99: TimeSpan.FromTicks(copy[Index(n, 0.99)]),
            Max: TimeSpan.FromTicks(max)
        );
    }

    private static int Index(int n, double percentile) => Math.Clamp((int)Math.Floor(n * percentile), 0, n - 1);
}
