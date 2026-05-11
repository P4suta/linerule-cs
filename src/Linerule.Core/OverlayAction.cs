namespace Linerule.Core;

/// <summary>
/// User-driven action targeting the overlay state machine. Closed sum.
/// Named <c>OverlayAction</c> rather than the bare <c>Action</c> to avoid the
/// CS0104 ambiguity with <see cref="System.Action"/> across consuming projects.
/// </summary>
public abstract record OverlayAction
{
    private protected OverlayAction() { }

    /// <summary>Advance <see cref="Mode"/> through its 4-cycle.</summary>
    public sealed record CycleMode : OverlayAction
    {
        public static CycleMode Instance { get; } = new();
    }

    /// <summary>
    /// Toggle the global visibility flag (mode is preserved). The hotkey
    /// layer's <c>HotkeyRepeater</c> wraps a single chord around two
    /// behaviors: a tap fires this once and lets the new state stick; a
    /// long-press (held ≥ 250 ms) fires it again on release, which
    /// undoes the flip — so the chord doubles as a press-and-peek
    /// gesture.
    /// </summary>
    public sealed record ToggleVisible : OverlayAction
    {
        public static ToggleVisible Instance { get; } = new();
    }

    /// <summary>Adjust thickness by a saturating delta.</summary>
    public sealed record BumpThickness(int Delta) : OverlayAction;

    /// <summary>Adjust opacity by a saturating delta.</summary>
    public sealed record BumpOpacity(int Delta) : OverlayAction;

    /// <summary>Emergency exit. Always handled by the platform layer, never by reduce.</summary>
    public sealed record Quit : OverlayAction
    {
        public static Quit Instance { get; } = new();
    }
}
