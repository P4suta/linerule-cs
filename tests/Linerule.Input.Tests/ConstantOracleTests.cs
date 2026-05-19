using Linerule.Core;
using Linerule.Input;

namespace Linerule.Input.Tests;

public sealed class ConstantOracleTests
{
    [Fact]
    public void AlwaysProgress_returns_true_for_every_action()
    {
        var o = ConstantOracle.AlwaysProgress;
        Assert.True(o.CanProgress(OverlayAction.CycleMode.Instance));
        Assert.True(o.CanProgress(OverlayAction.ToggleVisible.Instance));
        Assert.True(o.CanProgress(new OverlayAction.BumpThickness(1)));
        Assert.True(o.CanProgress(new OverlayAction.BumpOpacity(-1)));
        Assert.True(o.CanProgress(OverlayAction.Quit.Instance));
    }

    [Fact]
    public void AlwaysSaturated_returns_false_for_every_action()
    {
        var o = ConstantOracle.AlwaysSaturated;
        Assert.False(o.CanProgress(OverlayAction.CycleMode.Instance));
        Assert.False(o.CanProgress(OverlayAction.ToggleVisible.Instance));
        Assert.False(o.CanProgress(new OverlayAction.BumpThickness(1)));
        Assert.False(o.CanProgress(new OverlayAction.BumpOpacity(-1)));
        Assert.False(o.CanProgress(OverlayAction.Quit.Instance));
    }

    [Fact]
    public void Singletons_are_distinct_instances()
    {
        // Each static property is a separately-constructed singleton; they
        // share the type but not the seed. A regression that re-pointed
        // both at the same instance would also flip one of the assertions
        // above, but pinning identity guards against silent caching tricks.
        Assert.NotSame(ConstantOracle.AlwaysProgress, ConstantOracle.AlwaysSaturated);
    }

    [Fact]
    public void Custom_seed_returns_that_seed()
    {
        Assert.True(new ConstantOracle(answer: true).CanProgress(OverlayAction.Quit.Instance));
        Assert.False(new ConstantOracle(answer: false).CanProgress(OverlayAction.Quit.Instance));
    }
}
