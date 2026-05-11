namespace Linerule.Core;

/// <summary>
/// Pure state-machine reducer: <c>(state, action) → (next, delta)</c>. Persistent.
/// <para>
/// <see cref="OverlayAction.Quit"/> is intentionally a no-op here — process exit
/// is the platform layer's responsibility (the overlay HWND is <c>WS_EX_TRANSPARENT</c>
/// and cannot trap <c>WM_CLOSE</c>; the hotkey loop short-circuits).
/// </para>
/// </summary>
public static class Reduce
{
    public static (State Next, StateDelta Delta) Apply(State state, OverlayAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        return action switch
        {
            OverlayAction.CycleMode => CycleMode(state),
            OverlayAction.ToggleVisible => ToggleVisible(state),
            OverlayAction.BumpThickness bt => BumpThickness(state, bt.Delta),
            OverlayAction.BumpOpacity bo => BumpOpacity(state, bo.Delta),
            OverlayAction.Quit => (state, StateDelta.None),
            _ => throw new System.Diagnostics.UnreachableException(
                $"unknown OverlayAction variant: {action.GetType().Name}"
            ),
        };
    }

    private static (State, StateDelta) CycleMode(State state)
    {
        var nextMode = state.Mode.Cycle();
        return (state with { Mode = nextMode }, new StateDelta((Mode?)nextMode, Visible: null, ConfigChanged: false));
    }

    private static (State, StateDelta) ToggleVisible(State state)
    {
        var nextVisible = !state.Visible;
        return (state with { Visible = nextVisible }, new StateDelta(Mode: null, nextVisible, ConfigChanged: false));
    }

    private static (State, StateDelta) BumpThickness(State state, int delta)
    {
        // In Off mode the slit isn't rendered — thickness/opacity bumps are no-ops.
        // Returning None here lets HotkeyRepeater observe a saturating predicate
        // and stop polling (no wasted channel writes during a held-down chord).
        if (state.Mode == Mode.Off)
        {
            return (state, StateDelta.None);
        }
        var nextThickness = state.Config.Thickness.SaturatingAdd(delta);
        if (nextThickness == state.Config.Thickness)
        {
            return (state, StateDelta.None);
        }

        var nextConfig = state.Config with { Thickness = nextThickness };
        return (state with { Config = nextConfig }, new StateDelta(Mode: null, Visible: null, ConfigChanged: true));
    }

    private static (State, StateDelta) BumpOpacity(State state, int delta)
    {
        if (state.Mode == Mode.Off)
        {
            return (state, StateDelta.None);
        }
        var nextOpacity = state.Config.Opacity.SaturatingAdd(delta);
        if (nextOpacity == state.Config.Opacity)
        {
            return (state, StateDelta.None);
        }

        var nextConfig = state.Config with { Opacity = nextOpacity };
        return (state with { Config = nextConfig }, new StateDelta(Mode: null, Visible: null, ConfigChanged: true));
    }
}
