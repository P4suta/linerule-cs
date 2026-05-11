namespace Linerule.Core.Tests.Unit;

public sealed class RgbaTests
{
    [Fact]
    public void DefaultMask_is_pure_black_not_near_black()
    {
        // WinAppSDK + DComp gives true per-pixel alpha — pure black is correct.
        // See docs/adr/0009-transparency-via-dcomp.md.
        Assert.Equal(0, Rgba.DefaultMask.R);
        Assert.Equal(0, Rgba.DefaultMask.G);
        Assert.Equal(0, Rgba.DefaultMask.B);
        Assert.Equal(0xCC, Rgba.DefaultMask.A);
    }

    [Fact]
    public void WithAlpha_replaces_only_alpha()
    {
        var c = new Rgba(0x10, 0x20, 0x30, 0x40).WithAlpha(0xFF);
        Assert.Equal(new Rgba(0x10, 0x20, 0x30, 0xFF), c);
    }
}
