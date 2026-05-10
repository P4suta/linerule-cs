namespace Linerule.Core;

/// <summary>
/// Phantom marker for a coordinate space (logical / physical pixels).
/// C# dual of Rust's <c>struct Logical;</c> / <c>struct Physical;</c> phantom types.
/// Static-abstract member <see cref="Name"/> exists only to anchor the trait;
/// its real role is as a type-level token threaded through <see cref="Point{TSpace}"/>
/// and <see cref="ScreenRect{TSpace}"/> so that mixing logical and physical coords
/// becomes a compile-time error.
/// </summary>
public interface ICoordSpace
{
    static abstract string Name { get; }
}
