namespace Linerule.Config;

/// <summary>
/// Input-side tunables: per-tap step magnitudes and long-press repeat timings.
/// </summary>
public sealed record InputConfig(TapStepConfig TapStep, RepeatConfig Repeat)
{
    public static InputConfig Default { get; } = new(TapStep: TapStepConfig.Default, Repeat: RepeatConfig.Default);
}
