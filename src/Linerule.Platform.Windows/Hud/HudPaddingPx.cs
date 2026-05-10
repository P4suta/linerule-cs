using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>Inner padding around the HUD content (physical px).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudPaddingPx(float Edge, float Section, float Row)
{
    public static HudPaddingPx Default(float dpiScale) =>
        new(Edge: 14 * dpiScale, Section: 10 * dpiScale, Row: 4 * dpiScale);
}
