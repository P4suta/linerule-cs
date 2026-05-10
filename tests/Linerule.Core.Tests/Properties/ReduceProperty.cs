using FsCheck.Xunit;

namespace Linerule.Core.Tests.Properties;

public sealed class ReduceProperty
{
    private static OverlayAction MakeOverlayAction(byte variantIdx, int delta) =>
        (variantIdx % 5) switch
        {
            0 => OverlayAction.CycleMode.Instance,
            1 => OverlayAction.ToggleVisible.Instance,
            2 => new OverlayAction.BumpThickness(delta),
            3 => new OverlayAction.BumpOpacity(delta),
            _ => OverlayAction.Quit.Instance,
        };

    [Property]
    public bool Reduce_preserves_thickness_in_valid_range(byte variantIdx, int delta)
    {
        var (next, _) = Reduce.Apply(State.Default, MakeOverlayAction(variantIdx, delta));
        return next.Config.Thickness.Value is >= Thickness.MinValue and <= Thickness.MaxValue;
    }

    [Property]
    public bool Reduce_preserves_opacity_in_valid_range(byte variantIdx, int delta)
    {
        var (next, _) = Reduce.Apply(State.Default, MakeOverlayAction(variantIdx, delta));
        return next.Config.Opacity.Value is >= Opacity.MinValue and <= Opacity.MaxValue;
    }

    [Property]
    public bool Reduce_returns_same_state_iff_delta_is_none(byte variantIdx, int delta)
    {
        var (next, d) = Reduce.Apply(State.Default, MakeOverlayAction(variantIdx, delta));
        return ReferenceEquals(next, State.Default) == !d.IsAny;
    }
}
