using Linerule.Config;
using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Pure finite-state machine for hotkey hold patterns. The transition is a
/// total function <c>(state, input, config) → (state', effects)</c>; every
/// branch is exhaustive via pattern matching so any new
/// <see cref="HoldState"/> or <see cref="HoldInput"/> variant becomes a
/// compile error here instead of a silent fall-through.
/// </summary>
public static class HoldFsm
{
    public static (HoldState Next, IReadOnlyList<HoldEffect> Effects) Step(
        HoldState state,
        HoldInput input,
        RepeatConfig config
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(config);

        return (state, input) switch
        {
            (HoldState.Idle, HoldInput.Fired f) => OnFired(f, config),

            (HoldState.Repeating, HoldInput.Tick { StillHeld: false }) => (
                HoldState.Idle.Instance,
                [HoldEffect.Halt.Instance]
            ),

            (HoldState.Repeating r, HoldInput.Tick t) => OnRepeatTick(r, t, config),

            (HoldState.AwaitingRelease a, HoldInput.Tick { StillHeld: false } t) => OnAwaitingReleaseFinished(
                a,
                t,
                config
            ),

            (HoldState.AwaitingRelease, HoldInput.Tick _) => (
                state,
                [new HoldEffect.Schedule(TimeSpan.FromMilliseconds(config.ReleasePollMs))]
            ),

            // Any other (state, input) pair — e.g. a stray Fired while a
            // hold is in progress — collapses to a no-op transition; the
            // adapter dedup is "one chord drives one action" and shouldn't
            // produce overlapping Fired events. Defensive default keeps the
            // FSM total without raising in production.
            _ => (state, []),
        };
    }

    private static (HoldState Next, IReadOnlyList<HoldEffect> Effects) OnFired(HoldInput.Fired f, RepeatConfig cfg) =>
        f.Action switch
        {
            OverlayAction.BumpThickness t => BeginBumpRepeat(
                f.Chord,
                f.NowMs,
                Math.Sign(t.Delta),
                cfg.InitialDelayMs,
                sign => new OverlayAction.BumpThickness(sign)
            ),

            OverlayAction.BumpOpacity o => BeginBumpRepeat(
                f.Chord,
                f.NowMs,
                Math.Sign(o.Delta),
                cfg.InitialDelayMs,
                sign => new OverlayAction.BumpOpacity(sign)
            ),

            OverlayAction.CycleMode => (
                new HoldState.Repeating(f.Chord, OverlayAction.CycleMode.Instance, RepeatCadence.Slow, f.NowMs),
                [new HoldEffect.Schedule(TimeSpan.FromMilliseconds(cfg.InitialDelayMs))]
            ),

            OverlayAction.ToggleVisible => (
                new HoldState.AwaitingRelease(f.Chord, OverlayAction.ToggleVisible.Instance, f.NowMs),
                [new HoldEffect.Schedule(TimeSpan.FromMilliseconds(cfg.ReleasePollMs))]
            ),

            // Quit / unknown — not a hold target. Stay Idle, stop any timer.
            _ => (HoldState.Idle.Instance, [HoldEffect.Halt.Instance]),
        };

    private static (HoldState Next, IReadOnlyList<HoldEffect> Effects) BeginBumpRepeat(
        Platform.ChordSpec chord,
        long nowMs,
        int sign,
        int initialDelayMs,
        Func<int, OverlayAction> wrap
    )
    {
        // Math.Sign(0) == 0 → repeat would emit Delta=0 no-ops forever.
        if (sign == 0)
        {
            return (HoldState.Idle.Instance, [HoldEffect.Halt.Instance]);
        }
        var unitStep = wrap(sign);
        return (
            new HoldState.Repeating(chord, unitStep, RepeatCadence.Accelerating, nowMs),
            [new HoldEffect.Schedule(TimeSpan.FromMilliseconds(initialDelayMs))]
        );
    }

    private static (HoldState Next, IReadOnlyList<HoldEffect> Effects) OnRepeatTick(
        HoldState.Repeating r,
        HoldInput.Tick t,
        RepeatConfig cfg
    )
    {
        var heldMs = t.NowMs - r.StartedAtMs;
        var (interval, magnitude) = ComputeNextStep(heldMs, r.Cadence, cfg.SlowRepeatIntervalMs);
        var scaled = WithMagnitude(r.UnitStep, magnitude);
        if (!t.Oracle.CanProgress(scaled))
        {
            // Saturated at MIN/MAX or overlay is Off — nothing useful to emit.
            return (HoldState.Idle.Instance, [HoldEffect.Halt.Instance]);
        }
        return (r, [new HoldEffect.Enqueue(scaled), new HoldEffect.Schedule(interval)]);
    }

    private static (HoldState Next, IReadOnlyList<HoldEffect> Effects) OnAwaitingReleaseFinished(
        HoldState.AwaitingRelease a,
        HoldInput.Tick t,
        RepeatConfig cfg
    )
    {
        var heldMs = t.NowMs - a.StartedAtMs;
        if (heldMs >= cfg.LongPressThresholdMs)
        {
            return (HoldState.Idle.Instance, [new HoldEffect.Enqueue(a.UndoOnLongPress), HoldEffect.Halt.Instance]);
        }
        return (HoldState.Idle.Instance, [HoldEffect.Halt.Instance]);
    }

    /// <summary>Map hold-time-since-press onto (next repeat interval, step magnitude).</summary>
    public static (TimeSpan Interval, int StepMagnitude) ComputeNextStep(
        long heldMs,
        RepeatCadence cadence,
        int slowRepeatIntervalMs
    )
    {
        if (cadence == RepeatCadence.Slow)
        {
            return (TimeSpan.FromMilliseconds(slowRepeatIntervalMs), 1);
        }
        return heldMs switch
        {
            < 1000 => (TimeSpan.FromMilliseconds(50), 1), // 20 Hz × 1 — micro
            < 2000 => (TimeSpan.FromMilliseconds(25), 1), // 40 Hz × 1
            < 3000 => (TimeSpan.FromMilliseconds(16), 1), // 60 Hz × 1 — smooth vsync cap
            _ => (TimeSpan.FromMilliseconds(16), 4), // 60 Hz × 4 — sweep phase
        };
    }

    /// <summary>
    /// Scale a bump action's delta by the current step magnitude. Only the
    /// magnitude is varied; the sign carries through from the chord-bound
    /// direction (Thicker = +, Thinner = −).
    /// </summary>
    public static OverlayAction WithMagnitude(OverlayAction unitStep, int magnitude)
    {
        ArgumentNullException.ThrowIfNull(unitStep);
        return unitStep switch
        {
            OverlayAction.BumpThickness t => new OverlayAction.BumpThickness(t.Delta * magnitude),
            OverlayAction.BumpOpacity o => new OverlayAction.BumpOpacity(o.Delta * magnitude),
            _ => unitStep,
        };
    }
}
