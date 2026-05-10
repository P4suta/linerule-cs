using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>One paintable layer: a <see cref="Geometry"/> filled by a <see cref="Brush"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Layer(Geometry Geometry, Brush Brush)
{
    public static Layer SolidRect(ScreenRect<Logical> bounds, Rgba fill) =>
        new(new Geometry.Rect(bounds), new Brush.Solid(fill));
}
