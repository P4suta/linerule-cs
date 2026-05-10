using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>DPI-independent (logical) pixel space. The render() input space.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Logical : ICoordSpace
{
    public static string Name => "logical";
}
