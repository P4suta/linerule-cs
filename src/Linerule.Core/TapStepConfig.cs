namespace Linerule.Core;

/// <summary>
/// Per-tap (non-held) increment for thickness / opacity hotkey actions.
/// Long-press auto-repeat uses an independent accelerating curve that
/// multiplies these unit deltas — see <c>HoldFsm.ComputeNextStep</c> —
/// so making the tap step finer here does not slow down long-press
/// sweep, it only sharpens single-tap precision.
/// <para>
/// Opacity is intentionally finer than Thickness: ADR-0016 routes
/// rendered alpha through a perceptual (CIE L*) curve, which magnifies
/// the visible jump per linear step in the mid-range. Halving the tap
/// step (8 → 4) keeps mid-range single-tap adjustments inside what
/// the eye reads as a smooth nudge rather than a snap.
/// </para>
/// </summary>
public sealed record TapStepConfig(int Thickness, int Opacity)
{
    public static TapStepConfig Default { get; } = new(Thickness: 8, Opacity: 4);
}
