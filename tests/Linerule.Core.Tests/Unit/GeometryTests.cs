namespace Linerule.Core.Tests.Unit;

public sealed class GeometryTests
{
    private static readonly ScreenRect<Logical> Rect = new(new Point<Logical>(10, 20), 100, 200);

    [Fact]
    public void Rect_exposes_bounds_through_record_field()
    {
        var g = new Geometry.Rect(Rect);
        Assert.Equal(Rect, g.Bounds);
    }

    [Fact]
    public void Rect_pattern_match_binds_inner_bounds()
    {
        Geometry g = new Geometry.Rect(Rect);
        var bound = g switch
        {
            Geometry.Rect r => r.Bounds,
            _ => throw new InvalidOperationException("Geometry.Rect expected"),
        };
        Assert.Equal(Rect, bound);
    }

    [Fact]
    public void Record_equality_is_structural()
    {
        var a = new Geometry.Rect(Rect);
        var b = new Geometry.Rect(Rect);
        var c = new Geometry.Rect(new ScreenRect<Logical>(new Point<Logical>(0, 0), 1, 1));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Rect_is_sealed_variant_of_Geometry()
    {
        // Geometry is abstract with `private protected` ctor, so the only
        // way to construct one outside the namespace is via Geometry.Rect.
        // The IsType assertion pins the closed-DU shape: a static-typed
        // Geometry reference still resolves to Geometry.Rect at runtime.
        Geometry g = new Geometry.Rect(Rect);
        Assert.IsType<Geometry.Rect>(g);
    }
}
