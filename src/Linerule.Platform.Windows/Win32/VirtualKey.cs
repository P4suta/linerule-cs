using Linerule.Platform;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// Mapping from <see cref="KeyCode"/> to Win32 virtual-key code (<c>VK_*</c>).
/// Mirrors the Rust Windows adapter <c>spec_to_hotkey</c>.
/// </summary>
internal static class VirtualKey
{
    public const uint VK_BRACKETLEFT = 0xDB; // VK_OEM_4
    public const uint VK_BRACKETRIGHT = 0xDD; // VK_OEM_6
    public const uint VK_MINUS = 0xBD; // VK_OEM_MINUS
    public const uint VK_EQUAL = 0xBB; // VK_OEM_PLUS (a.k.a. '=')
    public const uint VK_LEFT = 0x25;
    public const uint VK_UP = 0x26;
    public const uint VK_RIGHT = 0x27;
    public const uint VK_DOWN = 0x28;

    public static uint FromKeyCode(KeyCode key) =>
        key switch
        {
            KeyCode.Letter l => l.Code,
            KeyCode.BracketLeft => VK_BRACKETLEFT,
            KeyCode.BracketRight => VK_BRACKETRIGHT,
            KeyCode.Minus => VK_MINUS,
            KeyCode.Equal => VK_EQUAL,
            KeyCode.ArrowUp => VK_UP,
            KeyCode.ArrowDown => VK_DOWN,
            KeyCode.ArrowLeft => VK_LEFT,
            KeyCode.ArrowRight => VK_RIGHT,
            _ => throw new System.Diagnostics.UnreachableException("unknown KeyCode variant"),
        };

    public static HOT_KEY_MODIFIERS FromModifiers(Modifiers mods)
    {
        HOT_KEY_MODIFIERS r = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (mods.Ctrl)
        {
            r |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        }
        if (mods.Alt)
        {
            r |= HOT_KEY_MODIFIERS.MOD_ALT;
        }
        if (mods.Shift)
        {
            r |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        }
        if (mods.Meta)
        {
            r |= HOT_KEY_MODIFIERS.MOD_WIN;
        }
        return r;
    }
}
