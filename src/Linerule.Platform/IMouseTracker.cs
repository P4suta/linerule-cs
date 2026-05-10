using Linerule.Core;

namespace Linerule.Platform;

/// <summary>
/// Polled cursor position. The Windows backend reads <c>GetCursorPos</c> per
/// pre-redraw tick; mock backends serve scripted positions.
/// </summary>
public interface IMouseTracker
{
    /// <summary>
    /// Current cursor in logical pixels in the active monitor's coordinate space.
    /// Returns null if the platform momentarily can't supply a position
    /// (e.g. session locked, RDP disconnect).
    /// </summary>
    Point<Logical>? Poll();
}
