using System;
using System.Collections.Immutable;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Diagnostics.Sinks;

namespace Linerule.Platform.Windows.Tests.Diagnostics;

public sealed class RingBufferSinkTests
{
    [Fact]
    public void NegativeOrZeroCapacity_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBufferSink(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBufferSink(-5));
    }

    [Fact]
    public void Snapshot_empty_when_no_writes()
    {
        var ring = new RingBufferSink(10);
        Assert.Empty(ring.Snapshot());
    }

    [Fact]
    public void Snapshot_preserves_chronological_order_below_capacity()
    {
        var ring = new RingBufferSink(5);
        for (var i = 0; i < 3; i++)
        {
            ring.Write(EntryWithStep($"step-{i}"));
        }
        var snap = ring.Snapshot();
        Assert.Equal(3, snap.Length);
        Assert.Equal("step-0", snap[0].Step);
        Assert.Equal("step-1", snap[1].Step);
        Assert.Equal("step-2", snap[2].Step);
    }

    [Fact]
    public void Snapshot_drops_oldest_after_wraparound()
    {
        var ring = new RingBufferSink(3);
        for (var i = 0; i < 5; i++)
        {
            ring.Write(EntryWithStep($"step-{i}"));
        }
        var snap = ring.Snapshot();
        Assert.Equal(3, snap.Length);
        // Last 3 (chronologically): step-2, step-3, step-4
        Assert.Equal("step-2", snap[0].Step);
        Assert.Equal("step-3", snap[1].Step);
        Assert.Equal("step-4", snap[2].Step);
    }

    [Fact]
    public void Snapshot_exactly_at_capacity_keeps_all_entries()
    {
        var ring = new RingBufferSink(4);
        for (var i = 0; i < 4; i++)
        {
            ring.Write(EntryWithStep($"step-{i}"));
        }
        var snap = ring.Snapshot();
        Assert.Equal(4, snap.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal($"step-{i}", snap[i].Step);
        }
    }

    [Fact]
    public void Snapshot_returns_independent_copy()
    {
        var ring = new RingBufferSink(3);
        ring.Write(EntryWithStep("first"));
        var snap1 = ring.Snapshot();
        ring.Write(EntryWithStep("second"));
        // snap1 must be unchanged by the second Write
        Assert.Single(snap1);
        Assert.Equal("first", snap1[0].Step);
    }

    private static LogEntry EntryWithStep(string step) =>
        new(
            Timestamp: new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero),
            Level: LogLevel.Info,
            Subsystem: "Test",
            Step: step,
            Context: new LogContext(Guid.Empty),
            Fields: ImmutableArray<LogField>.Empty,
            Exception: null);
}
