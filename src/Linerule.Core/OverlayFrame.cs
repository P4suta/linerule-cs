using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// One frame's worth of overlay geometry. v0.1 modes produce 0–2 layers; the
/// concrete layer count per mode is:
/// <list type="bullet">
///   <item><c>Off</c> — 0 layers</item>
///   <item><c>Bar</c> — 1 layer (the bar)</item>
///   <item><c>Mask</c> — 2 layers (top + bottom dim regions)</item>
///   <item><c>Vertical</c> — 1 layer (rotated bar)</item>
/// </list>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct OverlayFrame(ImmutableArray<Layer> Layers)
{
    public static OverlayFrame Empty { get; } = new([]);

    public bool IsEmpty => Layers.IsDefaultOrEmpty;

    public int LayerCount => Layers.IsDefault ? 0 : Layers.Length;
}
