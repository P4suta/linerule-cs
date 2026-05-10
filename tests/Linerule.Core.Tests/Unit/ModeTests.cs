namespace Linerule.Core.Tests.Unit;

public sealed class ModeTests
{
    [Theory]
    [InlineData(Mode.Off, Mode.Horizontal)]
    [InlineData(Mode.Horizontal, Mode.Vertical)]
    [InlineData(Mode.Vertical, Mode.Off)]
    public void Cycle_advances_to_next(Mode prev, Mode expected)
    {
        Assert.Equal(expected, prev.Cycle());
    }

    [Fact]
    public void Cycle_is_period_3()
    {
        // cycle³ ≡ id, exhaustively over the three valid values.
        foreach (var m in new[] { Mode.Off, Mode.Horizontal, Mode.Vertical })
        {
            Assert.Equal(m, m.Cycle().Cycle().Cycle());
        }
    }

    [Fact]
    public void Cycle_throws_on_cast_coerced_invalid_value()
    {
        // C# enums are open at the value level (cast-safety valve).
        // The defensive `_ => throw` arm is exercised via an out-of-range cast.
        const Mode bogus = (Mode)42;
        Assert.Throws<System.Diagnostics.UnreachableException>(() => bogus.Cycle());
    }
}
