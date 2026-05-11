namespace Linerule.Input;

/// <summary>
/// Cadence shape used while a chord is held in <see cref="HoldState.Repeating"/>.
/// <see cref="Accelerating"/> ramps 20 → 160 Hz over ≈3 s (designed for value
/// sweeps like Thicker / MoreOpaque); <see cref="Slow"/> is a flat fixed
/// interval (used for finite enums like Mode where rapid cycling would just
/// loop). The cadence schedule itself lives in
/// <see cref="HoldFsm.ComputeNextStep"/> so the FSM stays the single source
/// of truth.
/// </summary>
public enum RepeatCadence
{
    Accelerating,
    Slow,
}
