using Linerule.Core;

namespace Linerule.Platform.Mock;

/// <summary>Test double: serves a single, mutable cursor position.</summary>
public sealed class MockMouseTracker : IMouseTracker
{
    public Point<Logical>? Cursor { get; set; }

    public Point<Logical>? Poll() => Cursor;
}
