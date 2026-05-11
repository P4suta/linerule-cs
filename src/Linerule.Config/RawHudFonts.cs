namespace Linerule.Config;

internal sealed record RawHudFonts(
    float? Title,
    float? Status,
    float? Body,
    float? Telemetry,
    string? TitleFamily,
    string? MonoFamily,
    IReadOnlyList<string> UnknownKeys
);
