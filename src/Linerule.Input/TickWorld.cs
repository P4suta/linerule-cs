using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Per-tick world (the slice of mutable state the tick loop owns). Made
/// explicit so <see cref="TickPipeline.Step"/> can be a pure function — no
/// hidden capture of the host class's fields.
/// </summary>
public sealed record TickWorld(State State, Point<Logical>? LastCursor, long FrameSeq, long LastHudRefreshAtMs)
{
    public static TickWorld Initial { get; } = new(State.Default, LastCursor: null, FrameSeq: 0, LastHudRefreshAtMs: 0);
}
