namespace Linerule.Core.Tests.Unit;

public sealed class ThicknessTests
{
    [Fact]
    public void Default_is_28()
    {
        Assert.Equal(28, Thickness.Default.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(28)]
    [InlineData(512)]
    public void TryCreate_accepts_valid(int value)
    {
        var result = Thickness.TryCreate(value);
        var ok = Assert.IsType<Result<Thickness, CoreError>.Ok>(result);
        Assert.Equal((ushort)value, ok.Value.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    [InlineData(-5)]
    public void TryCreate_rejects_invalid(int value)
    {
        var result = Thickness.TryCreate(value);
        Assert.IsType<Result<Thickness, CoreError>.Err>(result);
    }

    [Theory]
    [InlineData(28, +4, 32)]
    [InlineData(28, -100, 1)]
    [InlineData(28, +1000, 512)]
    public void SaturatingAdd_clamps(int start, int delta, int expected)
    {
        var th = ((Result<Thickness, CoreError>.Ok)Thickness.TryCreate(start)).Value;
        Assert.Equal((ushort)expected, th.SaturatingAdd(delta).Value);
    }
}
