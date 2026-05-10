namespace Linerule.Core.Tests.Unit;

public sealed class OpacityTests
{
    [Fact]
    public void Default_is_AA()
    {
        Assert.Equal(0xAA, Opacity.Default.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void TryCreate_accepts_valid(int value)
    {
        var result = Opacity.TryCreate(value);
        var ok = Assert.IsType<Result<Opacity, CoreError>.Ok>(result);
        Assert.Equal((byte)value, ok.Value.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(int.MaxValue)]
    public void TryCreate_rejects_invalid(int value)
    {
        var result = Opacity.TryCreate(value);
        var err = Assert.IsType<Result<Opacity, CoreError>.Err>(result);
        var op = Assert.IsType<CoreError.Opacity>(err.Error);
        Assert.Equal(value, op.Given);
    }

    [Theory]
    [InlineData(170, +10, 180)]
    [InlineData(170, -200, 1)]
    [InlineData(170, +200, 255)]
    [InlineData(255, +1, 255)]
    [InlineData(1, -1, 1)]
    public void SaturatingAdd_clamps_to_valid_range(int start, int delta, int expected)
    {
        var op = ((Result<Opacity, CoreError>.Ok)Opacity.TryCreate(start)).Value;
        var bumped = op.SaturatingAdd(delta);
        Assert.Equal((byte)expected, bumped.Value);
    }
}
