namespace Linerule.Core.Tests.Unit;

public sealed class RenderTests
{
    private static readonly ScreenRect<Logical> Monitor = new(new Point<Logical>(0, 0), 1920, 1080);

    private static readonly Point<Logical> Cursor = new(960, 540);

    [Fact]
    public void Off_yields_empty_frame()
    {
        var frame = Render.Frame(Mode.Off, Cursor, Monitor, OverlayConfig.Default);
        Assert.True(frame.IsEmpty);
        Assert.Equal(0, frame.LayerCount);
    }

    [Fact]
    public void Horizontal_yields_three_layers_two_dim_plus_indicator()
    {
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default);
        Assert.Equal(3, frame.LayerCount);
        var thickness = OverlayConfig.Default.Thickness.Value;
        var top = ((Geometry.Rect)frame.Layers[0].Geometry).Bounds;
        var bottom = ((Geometry.Rect)frame.Layers[1].Geometry).Bounds;
        Assert.Equal(Monitor.Height, top.Height + bottom.Height + thickness);
    }

    [Fact]
    public void Vertical_yields_three_layers_two_dim_plus_indicator()
    {
        var frame = Render.Frame(Mode.Vertical, Cursor, Monitor, OverlayConfig.Default);
        Assert.Equal(3, frame.LayerCount);
        var thickness = OverlayConfig.Default.Thickness.Value;
        var left = ((Geometry.Rect)frame.Layers[0].Geometry).Bounds;
        var right = ((Geometry.Rect)frame.Layers[1].Geometry).Bounds;
        Assert.Equal(Monitor.Width, left.Width + right.Width + thickness);
    }

    [Fact]
    public void Horizontal_at_top_edge_clamps_inside_monitor()
    {
        var topCursor = new Point<Logical>(960, 0);
        var frame = Render.Frame(Mode.Horizontal, topCursor, Monitor, OverlayConfig.Default);
        foreach (var layer in frame.Layers)
        {
            Assert.True(Monitor.ContainsRect(((Geometry.Rect)layer.Geometry).Bounds));
        }
    }

    [Fact]
    public void Vertical_at_left_edge_clamps_inside_monitor()
    {
        var leftCursor = new Point<Logical>(0, 540);
        var frame = Render.Frame(Mode.Vertical, leftCursor, Monitor, OverlayConfig.Default);
        foreach (var layer in frame.Layers)
        {
            Assert.True(Monitor.ContainsRect(((Geometry.Rect)layer.Geometry).Bounds));
        }
    }

    [Fact]
    public void Mask_alpha_is_opacity_override_routed_through_perceptual_curve()
    {
        // ADR-0016: render-side opacity goes through PerceptualOpacity.Lstar,
        // so the byte fed into Rgba.WithAlpha is *not* the raw config value —
        // it's the L*-curved version. Asserting on the curved value rather
        // than the raw value pins the contract: user-set 50 surfaces as the
        // mathematically-derived perceptual byte (≈ 131 with the current
        // curve constants), and any change to the curve must be reflected
        // here.
        var opacity = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(50)).Value;
        var cfg = OverlayConfig.Default with { Opacity = opacity };
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, cfg);
        var solid = (Brush.Solid)frame.Layers[0].Brush;
        Assert.Equal(opacity.ToPerceptualByte(), solid.Color.A);
    }

    [Fact]
    public void Mask_alpha_curve_is_non_linear_at_midrange()
    {
        // Raw byte 128 → linear 0.502; Lstar(0.502) ≈ 0.762 → byte ≈ 194.
        // The whole point of the curve is that midrange != identity.
        var opacity = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(128)).Value;
        var cfg = OverlayConfig.Default with { Opacity = opacity };
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, cfg);
        var solid = (Brush.Solid)frame.Layers[0].Brush;
        Assert.NotEqual(128, solid.Color.A);
        Assert.InRange(solid.Color.A, 190, 200);
    }

    [Fact]
    public void Mask_alpha_endpoints_round_to_neighborhood_of_endpoints()
    {
        // Opacity has MinValue=1 (zero is reserved for "config not set"),
        // so the curve starts very close to zero, not at zero exactly.
        // Opacity=1 → linear ≈ 0.0039 (toe segment) → L* ≈ 0.0354 → byte ~9.
        // Opacity=255 → linear = 1.0 → L* = 1.0 → byte 255.
        var lo = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(1)).Value;
        var hi = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(255)).Value;
        var loFrame = Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default with { Opacity = lo });
        var hiFrame = Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default with { Opacity = hi });
        var loByte = ((Brush.Solid)loFrame.Layers[0].Brush).Color.A;
        var hiByte = ((Brush.Solid)hiFrame.Layers[0].Brush).Color.A;
        Assert.InRange(loByte, 0, 16);
        Assert.Equal(255, hiByte);
    }

    [Fact]
    public void Indicator_alpha_uses_perceptual_default()
    {
        // Indicator stripe = the third layer in any active-mode frame.
        // It seeds Opacity.IndicatorDefault (raw 0x80) and routes through
        // the same L* curve as the mask. The expected byte is fully derived
        // from Opacity.IndicatorDefault.ToPerceptualByte() so a curve tweak
        // updates this test in lockstep.
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default);
        var indicatorBrush = (Brush.Solid)frame.Layers[2].Brush;
        Assert.Equal(0xFF, indicatorBrush.Color.R);
        Assert.Equal(0xFF, indicatorBrush.Color.G);
        Assert.Equal(0xFF, indicatorBrush.Color.B);
        Assert.Equal(Opacity.IndicatorDefault.ToPerceptualByte(), indicatorBrush.Color.A);
    }

    [Fact]
    public void Indicator_alpha_is_lifted_above_raw_seed()
    {
        // 0x80 (= 128) raw → Lstar(0.502) → byte ≈ 194. If the indicator's
        // perceptual routing ever short-circuits to the raw seed, this
        // fails fast.
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default);
        var indicatorBrush = (Brush.Solid)frame.Layers[2].Brush;
        Assert.True(
            indicatorBrush.Color.A > 0x80,
            $"indicator alpha {indicatorBrush.Color.A} must exceed raw seed 0x80"
        );
    }
}
