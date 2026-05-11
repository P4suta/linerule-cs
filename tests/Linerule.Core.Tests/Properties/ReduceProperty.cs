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

    /// <summary>
    /// While <see cref="Mode.Off"/> is engaged, <see cref="OverlayAction.BumpThickness"/>
    /// and <see cref="OverlayAction.BumpOpacity"/> must leave <see cref="State"/> unchanged.
    /// <see cref="Input.ReduceOracle"/> relies on this to halt the long-press repeater;
    /// <see cref="Input.HudFadeKernel"/>'s Off-mode point-rect distance relies on a stable
    /// thickness for a stable fade radius.
    /// </summary>
    [Property]
    public bool Off_mode_blocks_thickness_and_opacity_bumps_for_any_state(
        int startThickness,
        int startOpacity,
        bool visible,
        int delta
    )
    {
        // Normalize the FsCheck-supplied seed into a valid State in Mode.Off.
        var thickness = (
            (Result<Thickness, CoreError>.Ok)
                Thickness.TryCreate(NormalizeIntoRange(startThickness, Thickness.MinValue, Thickness.MaxValue))
        ).Value;
        var opacity = (
            (Result<Opacity, CoreError>.Ok)
                Opacity.TryCreate(NormalizeIntoRange(startOpacity, Opacity.MinValue, Opacity.MaxValue))
        ).Value;
        var off = new State(
            Mode: Mode.Off,
            Visible: visible,
            Config: State.Default.Config with
            {
                Thickness = thickness,
                Opacity = opacity,
            }
        );

        var (afterT, dT) = Reduce.Apply(off, new OverlayAction.BumpThickness(delta));
        var (afterO, dO) = Reduce.Apply(off, new OverlayAction.BumpOpacity(delta));

        var thicknessUnchanged = ReferenceEquals(afterT, off) && !dT.IsAny;
        var opacityUnchanged = ReferenceEquals(afterO, off) && !dO.IsAny;
        return thicknessUnchanged && opacityUnchanged;
    }

    private static int NormalizeIntoRange(int candidate, int min, int max)
    {
        var span = max - min + 1;
        var folded = ((candidate % span) + span) % span;
        return min + folded;
    }
}
