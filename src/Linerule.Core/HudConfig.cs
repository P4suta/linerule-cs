namespace Linerule.Core;

/// <summary>
/// HUD-wide tunables. <see cref="BaseOpacity"/> is the steady-state translucency
/// applied to the entire HUD visual subtree (the tick-loop's cursor-distance
/// fade factor is multiplied by this base). <see cref="FadeDecayPx"/> tunes the
/// exponential decay for that cursor-distance fade.
/// <see cref="TelemetryRefreshMs"/> is the wall-clock cadence for FPS / drops /
/// timeouts telemetry refresh.
/// </summary>
public sealed record HudConfig(
    float BaseOpacity,
    float FadeDecayPx,
    long TelemetryRefreshMs,
    HudGeometry Geometry,
    HudPadding Padding,
    HudFonts Fonts,
    HudColors Colors
)
{
    public static HudConfig Default { get; } =
        new(
            BaseOpacity: 0.875f,
            FadeDecayPx: 120f,
            TelemetryRefreshMs: 200L,
            Geometry: HudGeometry.Default,
            Padding: HudPadding.Default,
            Fonts: HudFonts.Default,
            Colors: HudColors.Default
        );
}
