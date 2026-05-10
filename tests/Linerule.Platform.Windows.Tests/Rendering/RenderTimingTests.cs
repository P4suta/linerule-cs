using System;
using Linerule.Platform.Windows.Rendering;

namespace Linerule.Platform.Windows.Tests.Rendering;

public sealed class RenderTimingTests
{
    [Fact]
    public void ForRefreshHz_60_gives_120_Hz_tick_and_16_67_ms_budget()
    {
        var t = RenderTiming.ForRefreshHz(60);
        Assert.Equal(60, t.DisplayRefreshHz);
        Assert.Equal(120, t.TickRateHz);
        Assert.InRange(t.TickInterval.TotalMilliseconds, 8.32, 8.34);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 16.66, 16.68);
    }

    [Fact]
    public void ForRefreshHz_144_gives_250_Hz_tick_capped_not_288()
    {
        var t = RenderTiming.ForRefreshHz(144);
        Assert.Equal(144, t.DisplayRefreshHz);
        // 144 × 2 = 288 → capped at 250.
        Assert.Equal(250, t.TickRateHz);
        Assert.InRange(t.TickInterval.TotalMilliseconds, 3.99, 4.01);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 6.94, 6.95);
    }

    [Fact]
    public void ForRefreshHz_240_gives_250_Hz_tick_capped()
    {
        var t = RenderTiming.ForRefreshHz(240);
        Assert.Equal(240, t.DisplayRefreshHz);
        Assert.Equal(250, t.TickRateHz);
    }

    [Fact]
    public void ForRefreshHz_120_gives_240_Hz_tick_under_cap()
    {
        var t = RenderTiming.ForRefreshHz(120);
        Assert.Equal(240, t.TickRateHz);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ForRefreshHz_invalid_falls_back_to_60(int hz)
    {
        var t = RenderTiming.ForRefreshHz(hz);
        Assert.Equal(60, t.DisplayRefreshHz);
        Assert.Equal(120, t.TickRateHz);
    }

    [Fact]
    public void TickInterval_strictly_smaller_than_FrameBudget()
    {
        // The whole point of tick > 1× refresh is that we never miss a vsync.
        foreach (var hz in new[] { 30, 60, 90, 120, 144, 165, 240 })
        {
            var t = RenderTiming.ForRefreshHz(hz);
            Assert.True(
                t.TickInterval < t.FrameBudget,
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"refresh={hz}: tick={t.TickInterval.TotalMilliseconds}ms must be < budget={t.FrameBudget.TotalMilliseconds}ms"
                )
            );
        }
    }
}
