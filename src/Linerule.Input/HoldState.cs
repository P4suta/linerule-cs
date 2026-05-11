using Linerule.Core;
using Linerule.Platform;

namespace Linerule.Input;

/// <summary>
/// State of the long-press / repeat finite-state machine for one bound
/// chord. The closed sum makes every transition exhaustive — there is no
/// ambient <c>HoldKind</c> + nullable fields, "what fields are meaningful"
/// follows from the variant itself.
///
/// <list type="bullet">
///   <item>
///     <see cref="Idle"/> — no chord is being held. Identity element of the
///     state machine; <see cref="Idle.Instance"/> is the singleton.
///   </item>
///   <item>
///     <see cref="Repeating"/> — the chord is being held to drive a
///     <see cref="OverlayAction.BumpThickness"/> /
///     <see cref="OverlayAction.BumpOpacity"/> /
///     <see cref="OverlayAction.CycleMode"/> repeat train. The unit step
///     (already sign-normalized for the bumps) and the cadence are
///     baked in so the tick handler is a pure pattern match.
///   </item>
///   <item>
///     <see cref="AwaitingRelease"/> — the chord is being held for a
///     long-press-undo action (today only <see cref="OverlayAction.ToggleVisible"/>).
///     Nothing emits while held; release before
///     <c>RepeatConfig.LongPressThresholdMs</c> ⇒ no emit (the tap toggles
///     stick); release after ⇒ emit <see cref="UndoOnLongPress"/>.
///   </item>
/// </list>
/// </summary>
public abstract record HoldState
{
    private protected HoldState() { }

    public sealed record Idle : HoldState
    {
        public static Idle Instance { get; } = new();
    }

    public sealed record Repeating(ChordSpec Chord, OverlayAction UnitStep, RepeatCadence Cadence, long StartedAtMs)
        : HoldState;

    public sealed record AwaitingRelease(ChordSpec Chord, OverlayAction UndoOnLongPress, long StartedAtMs) : HoldState;
}
