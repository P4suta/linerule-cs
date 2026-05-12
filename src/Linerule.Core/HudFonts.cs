namespace Linerule.Core;

/// <summary>
/// HUD font sizes (logical px, scaled by DPI at render) and family names.
/// <see cref="TitleFamily"/> is the proportional UI font (titles + status rows);
/// <see cref="MonoFamily"/> is the monospace font used for numeric telemetry.
/// Per <c>feedback_directwrite_single_font_name</c>: DirectWrite does not parse
/// CSS-style fallback lists — these must be exact installed family names.
/// </summary>
public sealed record HudFonts(
    float Title,
    float Status,
    float Body,
    float Telemetry,
    string TitleFamily,
    string MonoFamily
)
{
    public static HudFonts Default { get; } =
        new(Title: 24f, Status: 22f, Body: 20f, Telemetry: 18f, TitleFamily: "Segoe UI", MonoFamily: "Cascadia Mono");
}
