using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>Font sizes for each text class in the HUD (physical px).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudFontSizesPx(float Title, float Status, float Body, float Telemetry)
{
    public static HudFontSizesPx Default(float dpiScale) =>
        new(Title: 14 * dpiScale, Status: 13 * dpiScale, Body: 12 * dpiScale, Telemetry: 11 * dpiScale);
}
