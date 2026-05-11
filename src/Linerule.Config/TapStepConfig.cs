namespace Linerule.Config;

/// <summary>
/// Per-tap (non-held) increment for thickness / opacity hotkey actions.
/// Long-press auto-repeat uses an independent accelerating curve and ignores
/// these values — see <c>HotkeyRepeater.ComputeNextStep</c>.
/// </summary>
public sealed record TapStepConfig(int Thickness, int Opacity)
{
    public static TapStepConfig Default { get; } = new(Thickness: 8, Opacity: 8);
}
