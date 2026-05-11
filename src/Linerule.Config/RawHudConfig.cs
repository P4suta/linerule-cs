namespace Linerule.Config;

internal sealed record RawHudConfig(
    float? BaseOpacity,
    float? FadeDecayPx,
    long? TelemetryRefreshMs,
    float? WidthLogical,
    float? HeightLogical,
    float? MarginLogical,
    RawHudPadding? Padding,
    RawHudFonts? Fonts,
    RawHudColors? Colors,
    IReadOnlyList<string> UnknownKeys
);
