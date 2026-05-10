using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// 2-D point in space <typeparamref name="TSpace"/>.
/// Mixing <c>Point&lt;Logical&gt;</c> with <c>Point&lt;Physical&gt;</c> is a compile error.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Point<TSpace>(int X, int Y)
    where TSpace : struct, ICoordSpace
{
    public override string ToString() => $"({X}, {Y}){TSpace.Name[0]}";
}
