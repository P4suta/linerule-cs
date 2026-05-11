using Linerule.Platform.Windows.Rendering;

namespace Linerule.Platform.Windows.Tests.Rendering;

public sealed class RenderTimingTests
{
    [Fact]
    public void ForRefreshHz_60_yields_16_67_ms_budget()
    {
        var t = RenderTiming.ForRefreshHz(60);
        Assert.Equal(60, t.DisplayRefreshHz);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 16.66, 16.68);
    }

    [Fact]
    public void ForRefreshHz_144_yields_6_94_ms_budget()
    {
        var t = RenderTiming.ForRefreshHz(144);
        Assert.Equal(144, t.DisplayRefreshHz);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 6.94, 6.95);
    }

    [Fact]
    public void ForRefreshHz_240_yields_4_17_ms_budget()
    {
        var t = RenderTiming.ForRefreshHz(240);
        Assert.Equal(240, t.DisplayRefreshHz);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 4.16, 4.17);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ForRefreshHz_invalid_falls_back_to_60(int hz)
    {
        var t = RenderTiming.ForRefreshHz(hz);
        Assert.Equal(60, t.DisplayRefreshHz);
        Assert.InRange(t.FrameBudget.TotalMilliseconds, 16.66, 16.68);
    }

    [Fact]
    public void FrameBudget_is_inverse_of_DisplayRefreshHz()
    {
        // 1 / refresh × 1000 ms — the single algebraic relationship the
        // record represents. Cover the practical refresh-rate spread.
        foreach (var hz in new[] { 30, 60, 90, 120, 144, 165, 240 })
        {
            var t = RenderTiming.ForRefreshHz(hz);
            Assert.InRange(t.FrameBudget.TotalMilliseconds, (1000.0 / hz) - 0.01, (1000.0 / hz) + 0.01);
        }
    }
}
