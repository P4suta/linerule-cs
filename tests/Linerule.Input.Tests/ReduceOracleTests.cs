using Linerule.Core;
using Linerule.Input;

namespace Linerule.Input.Tests;

public sealed class ReduceOracleTests
{
    [Fact]
    public void Null_snapshot_throws_at_construction_time()
    {
        Assert.Throws<ArgumentNullException>(() => new ReduceOracle(null!));
    }

    [Fact]
    public void Null_action_throws_on_CanProgress()
    {
        var o = new ReduceOracle(() => State.Default);
        Assert.Throws<ArgumentNullException>(() => o.CanProgress(null!));
    }

    [Fact]
    public void CanProgress_invokes_snapshot_each_call()
    {
        // The oracle has to read the *current* state, not a captured-at-
        // construction snapshot, so the long-press loop sees the same
        // saturation transitions a fresh Reduce.Apply would.
        var calls = 0;
        var o = new ReduceOracle(() =>
        {
            calls++;
            return State.Default;
        });
        o.CanProgress(OverlayAction.CycleMode.Instance);
        o.CanProgress(OverlayAction.CycleMode.Instance);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void CanProgress_reflects_delta_is_any_from_Reduce()
    {
        // State.Default is Mode.Off / Visible=true. CycleMode advances Mode
        // → delta emitted → oracle true. ToggleVisible flips Visible →
        // delta → oracle true. BumpThickness in Off mode is blocked (the
        // ReduceProperty Off-mode-block guarantee) → no delta → oracle false.
        var o = new ReduceOracle(() => State.Default);
        Assert.True(o.CanProgress(OverlayAction.CycleMode.Instance));
        Assert.True(o.CanProgress(OverlayAction.ToggleVisible.Instance));
        Assert.False(o.CanProgress(new OverlayAction.BumpThickness(1)));
    }

    [Fact]
    public void CanProgress_respects_snapshot_state_change()
    {
        // Wire the snapshot lambda to a mutable cell. State.Default is
        // Mode.Off (BumpThickness blocked). Moving the cell to Mode.Horizontal
        // unblocks the bump — oracle must reflect the new snapshot, not the
        // construction-time one.
        var stateCell = State.Default;
        var o = new ReduceOracle(() => stateCell);
        Assert.False(o.CanProgress(new OverlayAction.BumpThickness(1)));

        stateCell = stateCell with { Mode = Mode.Horizontal };
        Assert.True(o.CanProgress(new OverlayAction.BumpThickness(1)));
    }
}
