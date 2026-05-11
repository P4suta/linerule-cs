using System.Numerics;
using System.Runtime.InteropServices;
using Linerule.Config;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Geometry of the HUD panel — derived from monitor dimensions + DPI + the
/// user-configurable <see cref="HudGeometry"/> / <see cref="HudPadding"/> /
/// <see cref="HudFonts"/> from <see cref="HudConfig"/>. All values stored
/// here are physical pixels (the unit Composition draws in).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudLayout(
    Vector2 PositionPx,
    Vector2 SizePx,
    float DpiScale,
    HudPaddingPx Padding,
    HudFontSizesPx FontSizes
)
{
    public static HudLayout ForMonitor(int monitorWidthPx, int monitorMarginRightPx, float dpiScale, HudConfig hudCfg)
    {
        System.ArgumentNullException.ThrowIfNull(hudCfg);
        var widthPx = hudCfg.Geometry.WidthLogical * dpiScale;
        var heightPx = hudCfg.Geometry.HeightLogical * dpiScale;
        var marginPx = hudCfg.Geometry.MarginLogical * dpiScale;
        var x = monitorWidthPx - widthPx - marginPx + monitorMarginRightPx;
        var y = marginPx;
        return new HudLayout(
            PositionPx: new Vector2(x, y),
            SizePx: new Vector2(widthPx, heightPx),
            DpiScale: dpiScale,
            Padding: HudPaddingPx.From(hudCfg.Padding, dpiScale),
            FontSizes: HudFontSizesPx.From(hudCfg.Fonts, dpiScale)
        );
    }
}
