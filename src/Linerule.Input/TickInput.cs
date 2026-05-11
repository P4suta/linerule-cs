using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Per-tick inputs delivered by the platform adapter: the wall-clock time
/// stamp, the latest cursor sample (null = unknown, e.g. cursor outside the
/// overlay monitor), and the list of <see cref="OverlayAction"/>s drained
/// from the hotkey queue this tick.
/// </summary>
public sealed record TickInput(long NowMs, Point<Logical>? PolledCursor, IReadOnlyList<OverlayAction> DrainedHotkeys);
