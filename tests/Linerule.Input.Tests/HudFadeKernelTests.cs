using Linerule.Core;
using Linerule.Input;

namespace Linerule.Input.Tests;

/// <summary>
/// Behavior of <see cref="HudFadeKernel"/>: opacity reaches 1 at infinite
/// gap, 0 at full overlap, decays monotonically in between, and short-circuits
/// to 1 when no slit is currently drawn (Off / hidden).
/// </summary>
public sealed class HudFadeKernelTests
{
    private static readonly HudFadeKernel.HudBounds Bounds = new(Left: 100f, Top: 100f, Right: 200f, Bottom: 130f);

    private static State StateAt(Mode mode, bool visible, int thickness = 10, int opacity = 255)
    {
        var t = Thickness.TryCreate(thickness) is Result<Thickness, CoreError>.Ok okT ? okT.Value : Thickness.Default;
        var o = Opacity.TryCreate(opacity) is Result<Opacity, CoreError>.Ok okO ? okO.Value : Opacity.Default;
        var cfg = new OverlayConfig(new Rgba(0, 0, 0, 0xFF), t, o);
        return new State(mode, visible, cfg);
    }

    [Fact]
    public void AxisGapZeroOnOverlap()
    {
        Assert.Equal(0f, HudFadeKernel.AxisGap(0f, 10f, 5f, 15f));
        Assert.Equal(0f, HudFadeKernel.AxisGap(0f, 10f, 0f, 10f));
    }

    [Fact]
    public void AxisGapPositiveWhenSeparated()
    {
        Assert.Equal(5f, HudFadeKernel.AxisGap(0f, 10f, 15f, 20f));
        Assert.Equal(5f, HudFadeKernel.AxisGap(15f, 20f, 0f, 10f));
    }

    [Fact]
    public void HiddenStateYieldsInfiniteGap()
    {
        var state = StateAt(Mode.Horizontal, visible: false);
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(150, 200), Bounds);
        Assert.Equal(float.PositiveInfinity, gap);
    }

    [Fact]
    public void OffModeYieldsInfiniteGap()
    {
        var state = StateAt(Mode.Off, visible: true);
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(150, 200), Bounds);
        Assert.Equal(float.PositiveInfinity, gap);
    }

    [Fact]
    public void HorizontalModeMeasuresAlongYAxis()
    {
        var state = StateAt(Mode.Horizontal, visible: true, thickness: 10);
        // Cursor at y=200, slit covers y=[195,205]. HUD top=100,bottom=130 → gap = 195-130 = 65.
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(150, 200), Bounds);
        Assert.Equal(65f, gap);
    }

    [Fact]
    public void VerticalModeMeasuresAlongXAxis()
    {
        var state = StateAt(Mode.Vertical, visible: true, thickness: 10);
        // Cursor at x=250, slit covers x=[245,255]. HUD left=100,right=200 → gap = 245-200 = 45.
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(250, 50), Bounds);
        Assert.Equal(45f, gap);
    }

    [Fact]
    public void OpacityApproachesOneAtLargeDistance()
    {
        var state = StateAt(Mode.Horizontal, visible: true);
        var op = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(0, 10000), Bounds, fadeDecayPx: 50f);
        Assert.True(op > 0.99f);
    }

    [Fact]
    public void OpacityZeroAtFullOverlap()
    {
        var state = StateAt(Mode.Horizontal, visible: true, thickness: 200);
        // thick slit fully overlaps HUD vertically → gap = 0 → opacity = 1 - exp(0) = 0
        var op = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 115), Bounds, fadeDecayPx: 50f);
        Assert.Equal(0f, op);
    }

    [Fact]
    public void OpacityMonotoneInDistance()
    {
        var state = StateAt(Mode.Horizontal, visible: true, thickness: 10);
        var near = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 200), Bounds, fadeDecayPx: 50f);
        var far = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 400), Bounds, fadeDecayPx: 50f);
        Assert.True(far > near);
    }

    [Fact]
    public void DegenerateFadeDecayFallsBackToFullyVisible()
    {
        var state = StateAt(Mode.Horizontal, visible: true);
        var op = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 200), Bounds, fadeDecayPx: 0f);
        Assert.Equal(1f, op);
    }

    [Fact]
    public void OffOrHiddenAlwaysFullyVisible()
    {
        var off = StateAt(Mode.Off, visible: true);
        var hidden = StateAt(Mode.Horizontal, visible: false);
        Assert.Equal(1f, HudFadeKernel.ComputeOpacity(off, new Point<Logical>(150, 115), Bounds, 50f));
        Assert.Equal(1f, HudFadeKernel.ComputeOpacity(hidden, new Point<Logical>(150, 115), Bounds, 50f));
    }
}
