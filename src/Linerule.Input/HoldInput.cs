using Linerule.Core;
using Linerule.Platform;

namespace Linerule.Input;

/// <summary>
/// Inputs to <see cref="HoldFsm.Step"/>. Closed sum of the two events the
/// real hotkey adapter delivers:
/// <list type="bullet">
///   <item>
///     <see cref="Fired"/> — the OS just delivered a <c>WM_HOTKEY</c> for
///     the chord. The FSM decides whether this is a one-shot, a repeating
///     hold, or a long-press-undo arming.
///   </item>
///   <item>
///     <see cref="Tick"/> — the poll timer fired. The FSM consults
///     <see cref="ISaturationOracle"/> (for repeats) and the chord-held flag
///     to decide whether to emit, schedule another tick, or return to
///     <see cref="HoldState.Idle"/>.
///   </item>
/// </list>
/// </summary>
public abstract record HoldInput
{
    private protected HoldInput() { }

    public sealed record Fired(ChordSpec Chord, OverlayAction Action, long NowMs) : HoldInput;

    public sealed record Tick(long NowMs, bool StillHeld, ISaturationOracle Oracle) : HoldInput;
}
