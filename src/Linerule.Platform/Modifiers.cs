using System.Runtime.InteropServices;

namespace Linerule.Platform;

/// <summary>Modifier-key set ordered by Win32 convention: Ctrl, Alt, Shift, Meta (Win).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Modifiers(bool Ctrl, bool Alt, bool Shift, bool Meta)
{
    public static Modifiers None => default;

    public bool Any => Ctrl || Alt || Shift || Meta;
}
