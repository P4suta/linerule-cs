using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// Half-open rectangle <c>[origin.X, origin.X + width) × [origin.Y, origin.Y + height)</c>
/// in coordinate space <typeparamref name="TSpace"/>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ScreenRect<TSpace>(Point<TSpace> Origin, uint Width, uint Height)
    where TSpace : struct, ICoordSpace
{
    public int Left => Origin.X;

    public int Top => Origin.Y;

    // 64-bit widening + `checked` cast: monitor-pixel rectangles never
    // come close to int.MaxValue in practice, but a bogus
    // `ScreenRect(.., Width: uint.MaxValue)` (or a malformed Origin)
    // would silently wrap a plain `(int)Width` cast. `checked` converts
    // that into an OverflowException at the boundary instead of
    // poisoning every downstream geometric calculation.
    public int Right => checked((int)(Origin.X + (long)Width));

    public int Bottom => checked((int)(Origin.Y + (long)Height));

    /// <summary>True iff <paramref name="point"/> lies in the half-open rect.</summary>
    public bool Contains(Point<TSpace> point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>True iff <paramref name="inner"/> is fully contained (corners inclusive).</summary>
    public bool ContainsRect(ScreenRect<TSpace> inner) =>
        inner.Left >= Left && inner.Right <= Right && inner.Top >= Top && inner.Bottom <= Bottom;
}
