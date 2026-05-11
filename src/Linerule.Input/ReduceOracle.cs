using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Oracle that runs the pure <c>Reduce</c> against a state snapshot supplied
/// by a delegate. Use this in production: the delegate captures the live
/// state from <c>WindowsApp.TickLoop</c> and <see cref="CanProgress"/> returns
/// <c>Reduce.Apply(snapshot(), action).Delta.IsAny</c>.
/// </summary>
public sealed class ReduceOracle(Func<State> snapshot) : ISaturationOracle
{
    private readonly Func<State> _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public bool CanProgress(OverlayAction unitStep)
    {
        ArgumentNullException.ThrowIfNull(unitStep);
        var (_, delta) = Reduce.Apply(_snapshot(), unitStep);
        return delta.IsAny;
    }
}
