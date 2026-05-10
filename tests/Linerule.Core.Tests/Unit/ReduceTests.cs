namespace Linerule.Core.Tests.Unit;

public sealed class ReduceTests
{
    [Fact]
    public void CycleMode_advances_mode_and_reports_in_delta()
    {
        var (next, delta) = Reduce.Apply(State.Default, OverlayAction.CycleMode.Instance);
        Assert.Equal(Mode.Horizontal, next.Mode);
        Assert.Equal(Mode.Horizontal, delta.Mode);
        Assert.Null(delta.Visible);
        Assert.False(delta.ConfigChanged);
    }

    [Fact]
    public void ToggleVisible_flips_and_reports()
    {
        var (next, delta) = Reduce.Apply(State.Default, OverlayAction.ToggleVisible.Instance);
        Assert.False(next.Visible);
        Assert.Equal(false, delta.Visible);
        Assert.Null(delta.Mode);
    }

    [Fact]
    public void BumpThickness_changes_config_and_reports_change()
    {
        var (next, delta) = Reduce.Apply(State.Default, new OverlayAction.BumpThickness(+10));
        Assert.NotEqual(State.Default.Config.Thickness, next.Config.Thickness);
        Assert.Equal(38, next.Config.Thickness.Value);
        Assert.True(delta.ConfigChanged);
    }

    [Fact]
    public void BumpThickness_at_max_reports_no_change()
    {
        var maxState = State.Default with
        {
            Config = State.Default.Config with
            {
                Thickness = ((Result<Thickness, CoreError>.Ok)Thickness.TryCreate(512)).Value,
            },
        };
        var (next, delta) = Reduce.Apply(maxState, new OverlayAction.BumpThickness(+1));
        Assert.Same(maxState, next);
        Assert.False(delta.ConfigChanged);
        Assert.False(delta.IsAny);
    }

    [Fact]
    public void BumpOpacity_saturates_and_reports()
    {
        var lowState = State.Default with
        {
            Config = State.Default.Config with
            {
                Opacity = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(2)).Value,
            },
        };
        var (next, delta) = Reduce.Apply(lowState, new OverlayAction.BumpOpacity(-100));
        Assert.Equal(1, next.Config.Opacity.Value);
        Assert.True(delta.ConfigChanged);
    }

    [Fact]
    public void Quit_is_noop_for_state_machine()
    {
        var (next, delta) = Reduce.Apply(State.Default, OverlayAction.Quit.Instance);
        Assert.Same(State.Default, next);
        Assert.False(delta.IsAny);
    }
}
