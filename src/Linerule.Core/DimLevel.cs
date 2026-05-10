using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>Mask darkness level in <c>0..=255</c>. Total — every byte is valid.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct DimLevel(byte Value)
{
    public static DimLevel Default { get; } = new(0xCC);

    public override string ToString() => $"DimLevel({Value})";
}
