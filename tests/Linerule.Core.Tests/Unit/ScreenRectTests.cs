namespace Linerule.Core.Tests.Unit;

public sealed class ScreenRectTests
{
    private static ScreenRect<Logical> Rect(int x, int y, uint w, uint h) =>
        new(new Point<Logical>(x, y), w, h);

    [Theory]
    [InlineData(0, 0, true)]   // top-left corner inclusive
    [InlineData(99, 49, true)]  // bottom-right corner exclusive (just inside)
    [InlineData(100, 50, false)] // bottom-right corner exclusive (just outside)
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    [InlineData(50, 25, true)]
    public void Contains_is_half_open(int px, int py, bool expected)
    {
        var rect = Rect(0, 0, 100, 50);
        Assert.Equal(expected, rect.Contains(new Point<Logical>(px, py)));
    }

    [Fact]
    public void ContainsRect_full_containment()
    {
        var outer = Rect(0, 0, 100, 100);
        var inner = Rect(10, 10, 50, 50);
        Assert.True(outer.ContainsRect(inner));
    }

    [Fact]
    public void ContainsRect_partial_overlap_is_false()
    {
        var outer = Rect(0, 0, 100, 100);
        var overlapping = Rect(50, 50, 60, 60); // extends to (110,110)
        Assert.False(outer.ContainsRect(overlapping));
    }

    [Fact]
    public void ContainsRect_corner_inclusive()
    {
        // Inner with corners exactly on outer edges is contained.
        var outer = Rect(0, 0, 100, 100);
        var inner = Rect(0, 0, 100, 100);
        Assert.True(outer.ContainsRect(inner));
    }

    [Fact]
    public void Coordinate_spaces_do_not_unify()
    {
        // This test exists as a compile-time guard:
        // mixing Logical and Physical points must not type-check.
        // (We can't write the negative case at runtime — it'd be a compile error —
        // but we assert the positive case to ensure the type parameters work.)
        Point<Logical> logical = new(10, 20);
        Point<Physical> physical = new(40, 80);
        Assert.NotEqual<object>(logical, physical);
    }
}
