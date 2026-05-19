using Linerule.Platform;

namespace Linerule.Platform.Tests;

public sealed class ModifiersTests
{
    [Fact]
    public void None_is_default_struct_value()
    {
        // `default(Modifiers)` flows through every code path that doesn't
        // explicitly construct one (struct fields, dictionary lookup misses).
        // Pinning None to that value keeps "no modifier pressed" trivially
        // representable without needing a sentinel.
        Assert.Equal(default, Modifiers.None);
        Assert.False(Modifiers.None.Any);
    }

    [Fact]
    public void Any_is_true_when_at_least_one_modifier_pressed()
    {
        Assert.True(new Modifiers(Ctrl: true, Alt: false, Shift: false, Meta: false).Any);
        Assert.True(new Modifiers(Ctrl: false, Alt: true, Shift: false, Meta: false).Any);
        Assert.True(new Modifiers(Ctrl: false, Alt: false, Shift: true, Meta: false).Any);
        Assert.True(new Modifiers(Ctrl: false, Alt: false, Shift: false, Meta: true).Any);
        Assert.True(new Modifiers(Ctrl: true, Alt: true, Shift: true, Meta: true).Any);
    }

    [Fact]
    public void Record_equality_is_structural()
    {
        var a = new Modifiers(Ctrl: true, Alt: false, Shift: true, Meta: false);
        var b = new Modifiers(Ctrl: true, Alt: false, Shift: true, Meta: false);
        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { Alt = true });
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Field_order_is_ctrl_alt_shift_meta(bool ctrl, bool alt, bool shift, bool meta)
    {
        // Positional construction order is part of the contract — ChordParser
        // and the Win32 hotkey registration both rely on it. A field-rename
        // refactor that re-ordered fields would silently swap modifiers.
        var m = new Modifiers(Ctrl: ctrl, Alt: alt, Shift: shift, Meta: meta);
        Assert.Equal(ctrl, m.Ctrl);
        Assert.Equal(alt, m.Alt);
        Assert.Equal(shift, m.Shift);
        Assert.Equal(meta, m.Meta);
    }
}
