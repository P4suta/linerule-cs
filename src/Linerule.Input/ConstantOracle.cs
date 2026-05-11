using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Oracle whose answer is hard-coded. Useful in tests and in transition
/// states where saturation doesn't apply (long-press undo just polls for
/// release; nothing to saturate).
/// </summary>
public sealed class ConstantOracle(bool answer) : ISaturationOracle
{
    public bool CanProgress(OverlayAction unitStep) => answer;

    public static ConstantOracle AlwaysProgress { get; } = new(answer: true);
    public static ConstantOracle AlwaysSaturated { get; } = new(answer: false);
}
