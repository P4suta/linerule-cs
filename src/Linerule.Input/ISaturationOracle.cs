using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Capability the FSM consults to decide whether continuing to emit an
/// action would change observable state. The historical
/// <c>HotkeyRepeater._wouldAffectState</c> callback (which captured a closure
/// over <c>WindowsApp.TickLoop._state</c>) is replaced by this typed
/// interface — implementations stay pure (read-only over a state snapshot)
/// and the FSM stays free of platform plumbing.
/// </summary>
public interface ISaturationOracle
{
    /// <summary>
    /// <see langword="true"/> iff applying <paramref name="unitStep"/> against the
    /// current state would actually change something observable (delta
    /// non-empty). Returning <see langword="false"/> tells the FSM to stop polling —
    /// further emissions are guaranteed no-ops (e.g. value saturated at the
    /// MIN/MAX boundary, or the overlay sits in <see cref="Mode.Off"/>).
    /// </summary>
    bool CanProgress(OverlayAction unitStep);
}
