using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Font sizes for each text class in the HUD (physical px).
/// Sizes bumped 2026-05-11 on user request "HUDもっとくそでかい方が
/// よくね" — roughly ×1.7 across the board for a comfortably readable
/// status panel even at arm's length on a high-DPI display.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudFontSizesPx(float Title, float Status, float Body, float Telemetry)
{
    public static HudFontSizesPx Default(float dpiScale) =>
        new(Title: 24 * dpiScale, Status: 22 * dpiScale, Body: 20 * dpiScale, Telemetry: 18 * dpiScale);
}
