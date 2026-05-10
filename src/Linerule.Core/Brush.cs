namespace Linerule.Core;

/// <summary>
/// Render brush. Solid only in v0.1; gradients arrive when <see cref="Geometry.Rect"/>
/// alone stops being enough.
/// </summary>
public abstract record Brush
{
    private protected Brush() { }

    public sealed record Solid(Rgba Color) : Brush;
}
