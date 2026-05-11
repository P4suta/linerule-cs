using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>One frame's worth of overlay geometry: a list of layers to composite.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct OverlayFrame(ImmutableArray<Layer> Layers)
{
    public static OverlayFrame Empty { get; } = new([]);

    public bool IsEmpty => Layers.IsDefaultOrEmpty;

    public int LayerCount => Layers.IsDefault ? 0 : Layers.Length;
}
