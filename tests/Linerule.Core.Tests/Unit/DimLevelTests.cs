namespace Linerule.Core.Tests.Unit;

public sealed class DimLevelTests
{
    [Fact]
    public void Default_is_0xCC()
    {
        Assert.Equal(0xCC, DimLevel.Default.Value);
    }

    [Fact]
    public void Construction_is_total_across_all_bytes()
    {
        // The doc-string ("Total — every byte is valid") is the load-bearing
        // contract: there's no smart-constructor, every byte 0..=255 maps to
        // a valid DimLevel. Pin the endpoints to keep the property visible.
        Assert.Equal((byte)0, new DimLevel(0).Value);
        Assert.Equal((byte)255, new DimLevel(255).Value);
    }

    [Fact]
    public void Record_equality_is_structural()
    {
        Assert.Equal(new DimLevel(42), new DimLevel(42));
        Assert.NotEqual(new DimLevel(42), new DimLevel(43));
    }

    [Fact]
    public void ToString_uses_DimLevel_envelope()
    {
        Assert.Equal("DimLevel(170)", new DimLevel(170).ToString());
        Assert.Equal("DimLevel(204)", DimLevel.Default.ToString());
    }
}
