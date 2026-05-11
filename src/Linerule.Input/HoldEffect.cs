using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Side effects emitted by <see cref="HoldFsm.Step"/>. Returned as a small
/// list so the FSM can describe e.g. "emit this action AND schedule the
/// next tick AND stop the timer" without entangling those operations in
/// the platform-specific adapter. The adapter (<c>HotkeyRepeater</c>)
/// pattern-matches on the list to push to <see cref="System.Action"/>-like
/// sinks.
/// </summary>
public abstract record HoldEffect
{
    private protected HoldEffect() { }

    /// <summary>Enqueue an <see cref="OverlayAction"/> to the host queue.</summary>
    public sealed record Enqueue(OverlayAction Action) : HoldEffect;

    /// <summary>Reschedule the next FSM tick at the given interval.</summary>
    public sealed record Schedule(TimeSpan Next) : HoldEffect;

    /// <summary>Halt the poll timer; the FSM is in a terminal state.</summary>
    public sealed record Halt : HoldEffect
    {
        public static Halt Instance { get; } = new();
    }
}
