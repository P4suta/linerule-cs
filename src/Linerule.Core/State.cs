namespace Linerule.Core;

/// <summary>
/// Full overlay state. Reference record for cheap <c>with</c>-update;
/// <see cref="Reduce"/> is persistent so each transition produces a new <see cref="State"/>.
/// </summary>
public sealed record State(Mode Mode, bool Visible, OverlayConfig Config)
{
    public static State Default { get; } = new(
        Mode: Mode.Off,
        Visible: true,
        Config: OverlayConfig.Default);
}
