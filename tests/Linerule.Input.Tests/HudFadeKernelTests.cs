using System.Globalization;
using Linerule.Core;
using Linerule.Input;

namespace Linerule.Input.Tests;

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
    public void OffModeMeasuresEuclideanDistanceFromCursorPointRect()
    {
        // Cursor at (150, 200) with thickness=10 → cursor "point-rect" =
        // [145,155] × [195,205]. HUD = [100,200] × [100,130].
        // X-axis: cursor X-range inside HUD X-range → dx = 0.
        // Y-axis: cursor Y-range below HUD bottom → dy = 195 − 130 = 65.
        // result = √(0² + 65²) = 65.
        var state = StateAt(Mode.Off, visible: true);
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(150, 200), Bounds);
        Assert.Equal(65f, gap);
    }

    [Fact]
    public void OffModeZeroGapWhenCursorOverlapsHud()
    {
        // Cursor INSIDE HUD bounds → point-rect overlaps → gap = 0 → opacity 0.
        var state = StateAt(Mode.Off, visible: true);
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(150, 115), Bounds);
        Assert.Equal(0f, gap);
        var op = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 115), Bounds, fadeDecayPx: 50f);
        Assert.Equal(0f, op);
    }

    [Fact]
    public void OffModeFadesMonotonicallyOnCursorApproach()
    {
        // Same axis-proximity behavior Horizontal / Vertical exhibit, now
        // exercised in Off mode via the 2-D point-rect distance.
        var state = StateAt(Mode.Off, visible: true);
        var far = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 600), Bounds, fadeDecayPx: 50f);
        var near = HudFadeKernel.ComputeOpacity(state, new Point<Logical>(150, 150), Bounds, fadeDecayPx: 50f);
        Assert.True(far > near, string.Create(CultureInfo.InvariantCulture, $"far={far} should exceed near={near}"));
        Assert.True(far > 0.99f);
    }

    [Fact]
    public void OffModeDiagonalCornerDistanceUsesEuclideanCombine()
    {
        // Cursor diagonally off the HUD corner: both dx and dy positive,
        // result should be sqrt(dx² + dy²) — *not* max(dx, dy) or dx+dy.
        // thickness=10 → halfT=5.
        // Cursor (250, 200): X gap = 245 − 200 = 45. Y gap = 195 − 130 = 65.
        // expected = √(45² + 65²) = √(2025 + 4225) = √6250 ≈ 79.057.
        var state = StateAt(Mode.Off, visible: true);
        var gap = HudFadeKernel.SlitToHudGap(state, new Point<Logical>(250, 200), Bounds);
        Assert.InRange(gap, 79.0f, 79.1f);
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
    public void HiddenStateAlwaysFullyVisible()
    {
        // Only the explicit "hidden" path bypasses fade — every active mode
        // (including Off) participates in cursor-proximity fade.
        // Hidden = no slit, no point-rect, HUD fully visible.
        var hiddenHorizontal = StateAt(Mode.Horizontal, visible: false);
        var hiddenOff = StateAt(Mode.Off, visible: false);
        Assert.Equal(1f, HudFadeKernel.ComputeOpacity(hiddenHorizontal, new Point<Logical>(150, 115), Bounds, 50f));
        Assert.Equal(1f, HudFadeKernel.ComputeOpacity(hiddenOff, new Point<Logical>(150, 115), Bounds, 50f));
    }
}
