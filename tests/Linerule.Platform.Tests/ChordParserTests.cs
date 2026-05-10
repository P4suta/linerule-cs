using Linerule.Core;
using Linerule.Platform;

namespace Linerule.Platform.Tests;

public sealed class ChordParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R", true, true, false, false, 'R')]
    [InlineData("ctrl+alt+r", true, true, false, false, 'R')]
    [InlineData("Shift+A", false, false, true, false, 'A')]
    [InlineData("Win+Q", false, false, false, true, 'Q')]
    [InlineData("Ctrl+Shift+Alt+Win+Z", true, true, true, true, 'Z')]
    public void Parses_modifier_set_correctly(string input, bool ctrl, bool alt, bool shift, bool meta, char keyChar)
    {
        var result = ChordParser.Parse(input);
        var ok = Assert.IsType<Result<ChordSpec, ChordError>.Ok>(result);
        Assert.Equal(new Modifiers(ctrl, alt, shift, meta), ok.Value.Modifiers);
        var letter = Assert.IsType<KeyCode.Letter>(ok.Value.Key);
        Assert.Equal((byte)keyChar, letter.Code);
    }

    [Theory]
    [InlineData("Ctrl+Alt+]")]
    [InlineData("Ctrl+Alt+[")]
    [InlineData("Ctrl+Alt+-")]
    [InlineData("Ctrl+Alt+=")]
    public void Parses_punctuation_keys(string input)
    {
        var result = ChordParser.Parse(input);
        Assert.IsType<Result<ChordSpec, ChordError>.Ok>(result);
    }

    [Theory]
    [InlineData("Ctrl+Up", typeof(KeyCode.ArrowUp))]
    [InlineData("Ctrl+Down", typeof(KeyCode.ArrowDown))]
    [InlineData("Ctrl+Left", typeof(KeyCode.ArrowLeft))]
    [InlineData("Ctrl+Right", typeof(KeyCode.ArrowRight))]
    public void Parses_arrow_keys(string input, Type expectedKeyType)
    {
        var result = ChordParser.Parse(input);
        var ok = Assert.IsType<Result<ChordSpec, ChordError>.Ok>(result);
        Assert.IsType(expectedKeyType, ok.Value.Key);
    }

    [Fact]
    public void Empty_input_is_error()
    {
        Assert.IsType<Result<ChordSpec, ChordError>.Err>(ChordParser.Parse(""));
        Assert.IsType<Result<ChordSpec, ChordError>.Err>(ChordParser.Parse("   "));
    }

    [Fact]
    public void Modifiers_only_is_error()
    {
        var result = ChordParser.Parse("Ctrl+Alt");
        var err = Assert.IsType<Result<ChordSpec, ChordError>.Err>(result);
        Assert.IsType<ChordError.NoKey>(err.Error);
    }

    [Fact]
    public void Multiple_keys_is_error()
    {
        var result = ChordParser.Parse("Ctrl+A+B");
        var err = Assert.IsType<Result<ChordSpec, ChordError>.Err>(result);
        Assert.IsType<ChordError.MultipleKeys>(err.Error);
    }

    [Fact]
    public void Empty_token_is_error()
    {
        var result = ChordParser.Parse("Ctrl++R");
        var err = Assert.IsType<Result<ChordSpec, ChordError>.Err>(result);
        Assert.IsType<ChordError.EmptyToken>(err.Error);
    }

    [Fact]
    public void Unknown_token_is_error()
    {
        var result = ChordParser.Parse("Bogus+R");
        var err = Assert.IsType<Result<ChordSpec, ChordError>.Err>(result);
        Assert.IsType<ChordError.UnknownPart>(err.Error);
    }
}
