using System;
using Linerule.Platform.Windows.Rendering;

namespace Linerule.Platform.Windows.Tests.Rendering;

public sealed class RenderStatsTests
{
    [Fact]
    public void NegativeOrZeroCapacity_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStats(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStats(-3));
    }

    [Fact]
    public void Snapshot_with_no_records_returns_Empty_singleton()
    {
        var stats = new RenderStats();
        var snap = stats.Snapshot();
        Assert.Equal(0, snap.SampleCount);
        Assert.Equal(TimeSpan.Zero, snap.Mean);
        Assert.Equal(TimeSpan.Zero, snap.P99);
    }

    [Fact]
    public void Single_record_makes_min_mean_max_p99_all_equal()
    {
        var stats = new RenderStats(capacity: 10);
        stats.Record(TimeSpan.FromMilliseconds(5));
        var snap = stats.Snapshot();
        Assert.Equal(1, snap.SampleCount);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.Mean);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.P50);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.P95);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.P99);
        Assert.Equal(TimeSpan.FromMilliseconds(5), snap.Max);
    }

    [Fact]
    public void Percentiles_match_sorted_position()
    {
        var stats = new RenderStats(capacity: 100);
        // 100 frames: 1, 2, ..., 100 ms
        for (var i = 1; i <= 100; i++)
        {
            stats.Record(TimeSpan.FromMilliseconds(i));
        }
        var snap = stats.Snapshot();
        Assert.Equal(100, snap.SampleCount);
        Assert.Equal(TimeSpan.FromMilliseconds(1), snap.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(100), snap.Max);
        // p50 → index 50 in sorted [1..100] = 51 ms
        Assert.Equal(TimeSpan.FromMilliseconds(51), snap.P50);
        // p95 → index 95 = 96 ms
        Assert.Equal(TimeSpan.FromMilliseconds(96), snap.P95);
        // p99 → index 99 = 100 ms
        Assert.Equal(TimeSpan.FromMilliseconds(100), snap.P99);
    }

    [Fact]
    public void Records_beyond_capacity_drop_oldest_in_window()
    {
        var stats = new RenderStats(capacity: 5);
        // 1..10 — only the last 5 (6..10) should be in the window.
        for (var i = 1; i <= 10; i++)
        {
            stats.Record(TimeSpan.FromMilliseconds(i));
        }
        var snap = stats.Snapshot();
        Assert.Equal(5, snap.SampleCount);
        Assert.Equal(TimeSpan.FromMilliseconds(6), snap.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(10), snap.Max);
    }

    [Fact]
    public void TotalFrames_keeps_growing_past_capacity()
    {
        var stats = new RenderStats(capacity: 3);
        for (var i = 0; i < 10; i++)
        {
            stats.Record(TimeSpan.FromMilliseconds(i));
        }
        var snap = stats.Snapshot();
        Assert.Equal(3, snap.SampleCount); // window
        Assert.Equal(10, snap.TotalFrames); // cumulative
    }

    [Fact]
    public void RecordDrop_increments_DroppedFrames_count()
    {
        var stats = new RenderStats();
        stats.RecordDrop();
        stats.RecordDrop();
        stats.RecordDrop();
        Assert.Equal(3, stats.Snapshot().DroppedFrames);
    }
}
