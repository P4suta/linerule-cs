using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>Raw device-pixel space. Surface and HWND interop space.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Physical : ICoordSpace
{
    public static string Name => "physical";
}
