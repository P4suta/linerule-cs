namespace Linerule.Config;

/// <summary>
/// HUD panel geometry in logical (DPI-independent) pixels. Consumed by
/// <c>HudLayout.ForMonitor</c> alongside the per-monitor DPI scale to produce
/// physical-pixel dimensions.
/// </summary>
public sealed record HudGeometry(float WidthLogical, float HeightLogical, float MarginLogical)
{
    public static HudGeometry Default { get; } = new(WidthLogical: 520f, HeightLogical: 560f, MarginLogical: 24f);
}
