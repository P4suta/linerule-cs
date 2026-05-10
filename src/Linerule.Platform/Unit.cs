using System.Runtime.InteropServices;

namespace Linerule.Platform;

/// <summary>Marker placeholder for a successful operation that has no return data.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Unit
{
    public static Unit Value => default;
}
