namespace Linerule.Core;

/// <summary>
/// Render-budget + display-probe tunables. <see cref="WarnRatio"/> is the
/// fraction of the per-frame budget at which <c>RenderBudget</c> emits a warn
/// (default 0.8 ⇒ ~3 ms of vsync headroom on a 60 Hz display).
/// <see cref="FallbackRefreshHz"/> is the assumed refresh rate when the
/// DWM-side display probe fails.
/// </summary>
public sealed record RenderConfig(double WarnRatio, int FallbackRefreshHz)
{
    public static RenderConfig Default { get; } = new(WarnRatio: 0.8, FallbackRefreshHz: 60);
}
