using Linerule.Config;
using Linerule.Core;
using Linerule.Input;
using Linerule.Platform;

namespace Linerule.Input.Tests;

/// <summary>
/// Behaviour of the pure <see cref="HoldFsm.Step"/> reducer: every transition
/// is exhaustive (closed sum), <see cref="HoldState.Idle"/> is the
/// quiescent identity, sign-zero bumps short-circuit, saturation stops
/// repeats, and the long-press-undo timing matches the historic
/// <c>HotkeyRepeater</c> contract.
/// </summary>
public sealed class HoldFsmTests
{
    private static readonly RepeatConfig Cfg = RepeatConfig.Default;
    private static readonly ChordSpec Chord = new(default, new KeyCode.Letter((byte)'A'));

    [Fact]
    public void IdleIsFixedPointUnderTickWithoutPriorFired()
    {
        var input = new HoldInput.Tick(NowMs: 1000, StillHeld: true, ConstantOracle.AlwaysProgress);
        var (next, fx) = HoldFsm.Step(HoldState.Idle.Instance, input, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        Assert.Empty(fx);
    }

    [Fact]
    public void FiredBumpThicknessPositive_BeginsAcceleratingRepeat()
    {
        var fired = new HoldInput.Fired(Chord, new OverlayAction.BumpThickness(1), NowMs: 100);
        var (next, fx) = HoldFsm.Step(HoldState.Idle.Instance, fired, Cfg);
        var rep = Assert.IsType<HoldState.Repeating>(next);
        Assert.Equal(RepeatCadence.Accelerating, rep.Cadence);
        Assert.Equal(100, rep.StartedAtMs);
        Assert.IsType<HoldEffect.Schedule>(fx[0]);
    }

    [Fact]
    public void FiredBumpThicknessZero_StaysIdleAndStops()
    {
        var fired = new HoldInput.Fired(Chord, new OverlayAction.BumpThickness(0), NowMs: 100);
        var (next, fx) = HoldFsm.Step(HoldState.Idle.Instance, fired, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        Assert.Contains(fx, e => e is HoldEffect.Stop);
    }

    [Fact]
    public void FiredCycleMode_BeginsSlowRepeat()
    {
        var fired = new HoldInput.Fired(Chord, OverlayAction.CycleMode.Instance, NowMs: 100);
        var (next, _) = HoldFsm.Step(HoldState.Idle.Instance, fired, Cfg);
        var rep = Assert.IsType<HoldState.Repeating>(next);
        Assert.Equal(RepeatCadence.Slow, rep.Cadence);
    }

    [Fact]
    public void FiredToggleVisible_BeginsAwaitingRelease()
    {
        var fired = new HoldInput.Fired(Chord, OverlayAction.ToggleVisible.Instance, NowMs: 100);
        var (next, fx) = HoldFsm.Step(HoldState.Idle.Instance, fired, Cfg);
        var awaiting = Assert.IsType<HoldState.AwaitingRelease>(next);
        Assert.Equal(100, awaiting.StartedAtMs);
        Assert.IsType<HoldEffect.Schedule>(fx[0]);
    }

    [Fact]
    public void RepeatingTickStillHeldEmitsAndReschedules()
    {
        var rep = new HoldState.Repeating(Chord, new OverlayAction.BumpThickness(1), RepeatCadence.Accelerating, 0);
        var tick = new HoldInput.Tick(NowMs: 500, StillHeld: true, ConstantOracle.AlwaysProgress);
        var (next, fx) = HoldFsm.Step(rep, tick, Cfg);
        Assert.Same(rep, next);
        Assert.Contains(fx, e => e is HoldEffect.Enqueue);
        Assert.Contains(fx, e => e is HoldEffect.Schedule);
    }

    [Fact]
    public void RepeatingTickReleasedReturnsToIdle()
    {
        var rep = new HoldState.Repeating(Chord, new OverlayAction.BumpThickness(1), RepeatCadence.Accelerating, 0);
        var tick = new HoldInput.Tick(NowMs: 500, StillHeld: false, ConstantOracle.AlwaysProgress);
        var (next, fx) = HoldFsm.Step(rep, tick, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        Assert.Contains(fx, e => e is HoldEffect.Stop);
    }

    [Fact]
    public void SaturatedOracleStopsRepeating()
    {
        var rep = new HoldState.Repeating(Chord, new OverlayAction.BumpThickness(1), RepeatCadence.Accelerating, 0);
        var tick = new HoldInput.Tick(NowMs: 500, StillHeld: true, ConstantOracle.AlwaysSaturated);
        var (next, fx) = HoldFsm.Step(rep, tick, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        Assert.Contains(fx, e => e is HoldEffect.Stop);
        Assert.DoesNotContain(fx, e => e is HoldEffect.Enqueue);
    }

    [Fact]
    public void AwaitingReleaseWithinThreshold_NoUndoEmit()
    {
        var awaiting = new HoldState.AwaitingRelease(Chord, OverlayAction.ToggleVisible.Instance, StartedAtMs: 0);
        var earlyRelease = new HoldInput.Tick(NowMs: 50, StillHeld: false, ConstantOracle.AlwaysProgress);
        var (next, fx) = HoldFsm.Step(awaiting, earlyRelease, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        Assert.DoesNotContain(fx, e => e is HoldEffect.Enqueue);
        Assert.Contains(fx, e => e is HoldEffect.Stop);
    }

    [Fact]
    public void AwaitingReleasePastThreshold_EmitsUndo()
    {
        var awaiting = new HoldState.AwaitingRelease(Chord, OverlayAction.ToggleVisible.Instance, StartedAtMs: 0);
        var release = new HoldInput.Tick(
            NowMs: Cfg.LongPressThresholdMs + 1,
            StillHeld: false,
            ConstantOracle.AlwaysProgress
        );
        var (next, fx) = HoldFsm.Step(awaiting, release, Cfg);
        Assert.Same(HoldState.Idle.Instance, next);
        var emit = Assert.Single(fx.OfType<HoldEffect.Enqueue>());
        Assert.Same(OverlayAction.ToggleVisible.Instance, emit.Action);
    }

    [Fact]
    public void AwaitingReleaseStillHeld_KeepsPolling()
    {
        var awaiting = new HoldState.AwaitingRelease(Chord, OverlayAction.ToggleVisible.Instance, StartedAtMs: 0);
        var tick = new HoldInput.Tick(NowMs: 100, StillHeld: true, ConstantOracle.AlwaysProgress);
        var (next, fx) = HoldFsm.Step(awaiting, tick, Cfg);
        Assert.Equal(awaiting, next);
        Assert.IsType<HoldEffect.Schedule>(Assert.Single(fx));
    }

    [Fact]
    public void AcceleratingCadenceMatchesHistoricSchedule()
    {
        var (Interval, StepMagnitude) = HoldFsm.ComputeNextStep(
            500,
            RepeatCadence.Accelerating,
            slowRepeatIntervalMs: 400
        );
        Assert.Equal(TimeSpan.FromMilliseconds(50), Interval);
        Assert.Equal(1, StepMagnitude);

        var c2 = HoldFsm.ComputeNextStep(1500, RepeatCadence.Accelerating, slowRepeatIntervalMs: 400);
        Assert.Equal(TimeSpan.FromMilliseconds(25), c2.Interval);
        Assert.Equal(1, c2.StepMagnitude);

        var c3 = HoldFsm.ComputeNextStep(2500, RepeatCadence.Accelerating, slowRepeatIntervalMs: 400);
        Assert.Equal(TimeSpan.FromMilliseconds(16), c3.Interval);
        Assert.Equal(1, c3.StepMagnitude);

        var c4 = HoldFsm.ComputeNextStep(5000, RepeatCadence.Accelerating, slowRepeatIntervalMs: 400);
        Assert.Equal(TimeSpan.FromMilliseconds(16), c4.Interval);
        Assert.Equal(4, c4.StepMagnitude);
    }

    [Fact]
    public void SlowCadenceIsFlat()
    {
        var (Interval, StepMagnitude) = HoldFsm.ComputeNextStep(10_000, RepeatCadence.Slow, slowRepeatIntervalMs: 400);
        Assert.Equal(TimeSpan.FromMilliseconds(400), Interval);
        Assert.Equal(1, StepMagnitude);
    }

    [Fact]
    public void WithMagnitudeScalesBumps()
    {
        Assert.Equal(new OverlayAction.BumpThickness(4), HoldFsm.WithMagnitude(new OverlayAction.BumpThickness(1), 4));
        Assert.Equal(new OverlayAction.BumpOpacity(-3), HoldFsm.WithMagnitude(new OverlayAction.BumpOpacity(-1), 3));
        Assert.Same(OverlayAction.CycleMode.Instance, HoldFsm.WithMagnitude(OverlayAction.CycleMode.Instance, 7));
    }

    [Fact]
    public void ReduceOracleDelegatesToReduce()
    {
        var t = Thickness.TryCreate(28) is Result<Thickness, CoreError>.Ok ok ? ok.Value : Thickness.Default;
        var o = Opacity.TryCreate(0xAA) is Result<Opacity, CoreError>.Ok okO ? okO.Value : Opacity.Default;
        var state = new State(Mode.Horizontal, Visible: true, Config: new OverlayConfig(new Rgba(0, 0, 0, 0xFF), t, o));
        var oracle = new ReduceOracle(() => state);
        Assert.True(oracle.CanProgress(new OverlayAction.BumpThickness(1)));

        var offState = state with { Mode = Mode.Off };
        var offOracle = new ReduceOracle(() => offState);
        // In Mode.Off, BumpThickness is a domain no-op (no slit drawn).
        Assert.False(offOracle.CanProgress(new OverlayAction.BumpThickness(1)));
    }
}
