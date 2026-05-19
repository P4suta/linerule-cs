namespace Linerule.Core.Tests.Unit;

public sealed class HudPaddingTests
{
    [Fact]
    public void Default_values_match_documented_layout()
    {
        // Edge / Section / Row constants are referenced by HudLayout to size
        // every HUD element — drift here would silently re-flow the panel.
        var d = HudPadding.Default;
        Assert.Equal(24f, d.Edge);
        Assert.Equal(16f, d.Section);
        Assert.Equal(8f, d.Row);
    }

    [Fact]
    public void Record_equality_is_structural()
    {
        var a = new HudPadding(1f, 2f, 3f);
        var b = new HudPadding(1f, 2f, 3f);
        var c = a with { Row = 99f };
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void With_expression_replaces_only_named_field()
    {
        var d = HudPadding.Default with { Edge = 100f };
        Assert.Equal(100f, d.Edge);
        Assert.Equal(HudPadding.Default.Section, d.Section);
        Assert.Equal(HudPadding.Default.Row, d.Row);
    }
}
