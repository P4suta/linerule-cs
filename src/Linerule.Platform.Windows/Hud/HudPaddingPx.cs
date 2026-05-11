using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Inner padding around the HUD content (physical px). Bumped ×1.7
/// alongside the font-size enlargement so the proportions stay sane on
/// the larger panel.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudPaddingPx(float Edge, float Section, float Row)
{
    public static HudPaddingPx Default(float dpiScale) =>
        new(Edge: 24 * dpiScale, Section: 16 * dpiScale, Row: 8 * dpiScale);
}
