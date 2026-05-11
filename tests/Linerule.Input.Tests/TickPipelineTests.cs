using Linerule.Core;
using Linerule.Input;

namespace Linerule.Input.Tests;

/// <summary>
/// Behavior of the pure <see cref="TickPipeline.Step"/> reducer. Mirrors the
/// historical <c>WindowsApp.TickLoop.OnTick</c> contract: Quit short-circuits,
/// state changes redraw the overlay and refresh the HUD, cursor moves drive
/// HUD opacity follow, and the HUD telemetry refresh happens on a wall-clock
/// cadence independent of frame rate.
/// </summary>
public sealed class TickPipelineTests
{
    private static State StateAt(Mode mode = Mode.Horizontal, bool visible = true)
    {
        var t = Thickness.TryCreate(28) is Result<Thickness, CoreError>.Ok okT ? okT.Value : Thickness.Default;
        var o = Opacity.TryCreate(0xAA) is Result<Opacity, CoreError>.Ok okO ? okO.Value : Opacity.Default;
        return new State(mode, visible, new OverlayConfig(new Rgba(0, 0, 0, 0xFF), t, o));
    }

    private static TickWorld WorldAt(State state, Point<Logical>? cursor = null, long frameSeq = 0, long lastHud = 0) =>
        new(state, cursor, frameSeq, lastHud);

    [Fact]
    public void QuitShortCircuitsTick()
    {
        var world = WorldAt(StateAt());
        var input = new TickInput(
            NowMs: 1000,
            PolledCursor: new Point<Logical>(10, 10),
            DrainedHotkeys: [OverlayAction.Quit.Instance]
        );
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 100);
        Assert.Same(world, next);
        Assert.Single(fx);
        Assert.IsType<TickEffect.Quit>(fx[0]);
    }

    [Fact]
    public void StateChangeProducesDrawAndRefreshHud()
    {
        var world = WorldAt(StateAt(Mode.Off), new Point<Logical>(10, 10));
        var input = new TickInput(
            NowMs: 1000,
            PolledCursor: new Point<Logical>(10, 10),
            DrainedHotkeys: [OverlayAction.CycleMode.Instance]
        );
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 100);
        Assert.Equal(Mode.Horizontal, next.State.Mode);
        Assert.Contains(fx, e => e is TickEffect.DrawOverlay);
        Assert.Contains(fx, e => e is TickEffect.RefreshHud);
        Assert.Contains(fx, e => e is TickEffect.LogStateChanged);
    }

    [Fact]
    public void CursorMoveTriggersHudOpacityRefresh()
    {
        var world = WorldAt(StateAt(), new Point<Logical>(0, 0));
        var input = new TickInput(NowMs: 1000, PolledCursor: new Point<Logical>(100, 200), DrainedHotkeys: []);
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 1000);
        Assert.Equal(new Point<Logical>(100, 200), next.LastCursor);
        Assert.Contains(fx, e => e is TickEffect.SetHudOpacity);
        Assert.Contains(fx, e => e is TickEffect.DrawOverlay);
    }

    [Fact]
    public void NoCursorMoveNoEffectsOnQuietTick()
    {
        var world = WorldAt(StateAt(), new Point<Logical>(5, 5), lastHud: 100);
        var input = new TickInput(NowMs: 150, PolledCursor: new Point<Logical>(5, 5), DrainedHotkeys: []);
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 1000);
        Assert.Equal(world.State, next.State);
        Assert.Empty(fx);
    }

    [Fact]
    public void TelemetryRefreshFiresAfterIntervalElapses()
    {
        var world = WorldAt(StateAt(), new Point<Logical>(5, 5), lastHud: 100);
        var input = new TickInput(NowMs: 1500, PolledCursor: new Point<Logical>(5, 5), DrainedHotkeys: []);
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 200);
        Assert.Contains(fx, e => e is TickEffect.RefreshHud);
        Assert.Equal(1500, next.LastHudRefreshAtMs);
    }

    [Fact]
    public void OffModeRedrawClearsOverlay()
    {
        var world = WorldAt(StateAt(Mode.Horizontal), new Point<Logical>(5, 5));
        var input = new TickInput(
            NowMs: 1000,
            PolledCursor: new Point<Logical>(5, 5),
            DrainedHotkeys:
            [
                OverlayAction.CycleMode.Instance,
                OverlayAction.CycleMode.Instance,
                OverlayAction.CycleMode.Instance,
            ]
        );
        // 3× CycleMode brings us back to start: Horizontal → Vertical → Off → Horizontal
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 1000);
        Assert.Equal(Mode.Horizontal, next.State.Mode);
        Assert.Contains(fx, e => e is TickEffect.DrawOverlay);
    }

    [Fact]
    public void HiddenStateRedrawClearsOverlay()
    {
        var world = WorldAt(StateAt(Mode.Horizontal, visible: true), new Point<Logical>(5, 5));
        var input = new TickInput(
            NowMs: 1000,
            PolledCursor: new Point<Logical>(5, 5),
            DrainedHotkeys: [OverlayAction.ToggleVisible.Instance]
        );
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 1000);
        Assert.False(next.State.Visible);
        Assert.Contains(fx, e => e is TickEffect.ClearOverlay);
    }

    [Fact]
    public void FrameSeqIncrementsOnRedraw()
    {
        var world = WorldAt(StateAt(), new Point<Logical>(0, 0), frameSeq: 100);
        var input = new TickInput(NowMs: 1000, PolledCursor: new Point<Logical>(10, 10), DrainedHotkeys: []);
        var (next, _) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 1000);
        Assert.Equal(101, next.FrameSeq);
    }

    [Fact]
    public void NoStateChangeSkipsLogStateChangedEffect()
    {
        // Mode.Off + BumpThickness is a domain no-op (Reduce returns no delta)
        var world = WorldAt(StateAt(Mode.Off, visible: true));
        var input = new TickInput(
            NowMs: 1000,
            PolledCursor: null,
            DrainedHotkeys: [new OverlayAction.BumpThickness(1)]
        );
        var (next, fx) = TickPipeline.Step(world, input, telemetryRefreshIntervalMs: 100_000);
        Assert.Equal(world.State, next.State);
        Assert.DoesNotContain(fx, e => e is TickEffect.LogStateChanged);
    }
}
