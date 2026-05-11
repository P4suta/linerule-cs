using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Pure transition: <c>(world, input, telemetryRefreshMs) → (world', effects)</c>.
/// Mirrors the historical <c>WindowsApp.TickLoop.OnTick</c> body verbatim —
/// hotkey drain via <see cref="Reduce.Apply"/>, overlay redraw policy based
/// on state-change OR cursor-move while visible, HUD opacity follow on
/// cursor moves, and HUD telemetry refresh on a wall-clock cadence — but
/// expressed without any <c>OverlayWindow</c> / <c>HudVisual</c> /
/// <c>RenderClock</c> references, so the policy is testable from
/// <c>Linerule.Input.Tests</c> in isolation.
/// </summary>
public static class TickPipeline
{
    /// <summary>Intermediate result of draining hotkey actions for a single tick.</summary>
    private readonly record struct HotkeyDrain(State Next, bool AnyStateChange, bool Quit, List<TickEffect> Effects);

    public static (TickWorld Next, IReadOnlyList<TickEffect> Effects) Step(
        TickWorld world,
        TickInput input,
        long telemetryRefreshIntervalMs
    )
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(input);

        var drain = DrainHotkeys(world.State, input.DrainedHotkeys);
        if (drain.Quit)
        {
            // Match TickLoop.OnTick: on Quit, abort the rest of the
            // tick without polling cursor / refreshing HUD.
            return (world, drain.Effects);
        }

        var state = drain.Next;
        var effects = drain.Effects;

        var (lastCursor, cursorMoved) = AdvanceCursor(world.LastCursor, input.PolledCursor);

        var frameSeq = world.FrameSeq;
        if (drain.AnyStateChange || cursorMoved)
        {
            frameSeq = EmitOverlay(state, lastCursor, frameSeq, effects);
        }

        if (cursorMoved && lastCursor is { } c2)
        {
            effects.Add(new TickEffect.SetHudOpacity(state, c2));
        }

        var lastHud = MaybeRefreshHud(
            state,
            world.LastHudRefreshAtMs,
            input.NowMs,
            drain.AnyStateChange,
            telemetryRefreshIntervalMs,
            effects
        );

        return (new TickWorld(state, lastCursor, frameSeq, lastHud), effects);
    }

    private static HotkeyDrain DrainHotkeys(State initial, IReadOnlyList<OverlayAction> actions)
    {
        var effects = new List<TickEffect>();
        var state = initial;
        var anyStateChange = false;

        foreach (var action in actions)
        {
            if (action is OverlayAction.Quit)
            {
                effects.Add(TickEffect.Exit.Instance);
                return new HotkeyDrain(state, anyStateChange, Quit: true, effects);
            }
            var (next, delta) = Reduce.Apply(state, action);
            state = next;
            if (delta.IsAny)
            {
                anyStateChange = true;
                effects.Add(new TickEffect.LogStateChanged(action, state.Mode, state.Visible));
            }
        }

        return new HotkeyDrain(state, anyStateChange, Quit: false, effects);
    }

    private static (Point<Logical>? LastCursor, bool Moved) AdvanceCursor(
        Point<Logical>? previous,
        Point<Logical>? polled
    )
    {
        var moved = polled is { } c && !PointEquals(c, previous);
        return (moved ? polled : previous, moved);
    }

    /// <summary>
    /// Emit the overlay draw/clear effect for this tick. Returns the new
    /// frame counter (incremented iff a frame was actually drawn).
    /// </summary>
    private static long EmitOverlay(State state, Point<Logical>? lastCursor, long frameSeq, List<TickEffect> effects)
    {
        if (state.Visible && state.Mode != Mode.Off && lastCursor is { } drawCursor)
        {
            effects.Add(new TickEffect.DrawOverlay(state.Mode, drawCursor, state.Config));
            return frameSeq + 1;
        }
        effects.Add(TickEffect.ClearOverlay.Instance);
        return frameSeq;
    }

    /// <summary>
    /// HUD refresh policy: refresh either when state changed (snap to it)
    /// or when the telemetry interval has elapsed (steady cadence). Both
    /// branches collapse to the same emit because their semantics are
    /// identical at this layer — the renderer reads the current state
    /// either way (S1871 is satisfied by the single emit site).
    /// </summary>
    private static long MaybeRefreshHud(
        State state,
        long lastHudRefreshAtMs,
        long nowMs,
        bool anyStateChange,
        long telemetryRefreshIntervalMs,
        List<TickEffect> effects
    )
    {
        var elapsed = nowMs - lastHudRefreshAtMs;
        if (!anyStateChange && elapsed < telemetryRefreshIntervalMs)
        {
            return lastHudRefreshAtMs;
        }
        effects.Add(new TickEffect.RefreshHud(state));
        return nowMs;
    }

    private static bool PointEquals(Point<Logical> a, Point<Logical>? b) => b is { } c && a.X == c.X && a.Y == c.Y;
}
