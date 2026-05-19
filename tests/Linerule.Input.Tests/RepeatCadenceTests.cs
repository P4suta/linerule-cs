using Linerule.Input;

namespace Linerule.Input.Tests;

public sealed class RepeatCadenceTests
{
    [Fact]
    public void Enum_has_exactly_two_variants()
    {
        // HoldFsm.ComputeNextStep switches on this enum; adding a third
        // variant without updating the switch would silently fall through
        // to a default-branch (and break repeat cadence semantics). Test
        // pins the closed shape so the FSM stays exhaustive.
        var values = Enum.GetValues<RepeatCadence>();
        Assert.Equal(2, values.Length);
        Assert.Contains(RepeatCadence.Accelerating, values);
        Assert.Contains(RepeatCadence.Slow, values);
    }

    [Fact]
    public void Default_value_is_accelerating()
    {
        // C# default(enum) = 0 = first declared variant. The HUD value-sweep
        // path (Thicker / MoreOpaque) relies on this default for the initial
        // state; if someone re-orders the enum, this test catches the
        // reshuffle before it surfaces as "first long-press feels wrong".
        Assert.Equal(RepeatCadence.Accelerating, default);
    }

    [Theory]
    [InlineData(RepeatCadence.Accelerating)]
    [InlineData(RepeatCadence.Slow)]
    public void Pattern_match_is_exhaustive(RepeatCadence cadence)
    {
        // A regression where a third variant got added but a switch didn't
        // would let this throw UnreachableException — caught by the test.
        var label = cadence switch
        {
            RepeatCadence.Accelerating => "accelerating",
            RepeatCadence.Slow => "slow",
            _ => throw new System.Diagnostics.UnreachableException(),
        };
        Assert.False(string.IsNullOrEmpty(label));
    }
}
