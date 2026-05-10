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
    public void Mask_uses_opacity_override_on_MaskColor_alpha()
    {
        var cfg = OverlayConfig.Default with { Opacity = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(50)).Value };
        var frame = Render.Frame(Mode.Horizontal, Cursor, Monitor, cfg);
        var solid = (Brush.Solid)frame.Layers[0].Brush;
        Assert.Equal(50, solid.Color.A);
    }
}
