using System.Numerics;
using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Geometry of the HUD panel — derived from monitor dimensions + DPI so
/// the panel sits at a sensible top-right offset and at a readable size
/// across 1080p / 1440p / 4K. All values are in physical pixels (the unit
/// Win2D / Composition draw in).
///
/// <para>
/// <b>Sizing policy</b>: width = 320 logical px = 320 × DPI / 96 physical
/// px. Height grows with content (set by the renderer after measuring).
/// Margin from the monitor edge is 16 logical px.
/// </para>
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
    private const float WidthLogical = 300f;
    private const float HeightLogical = 320f;
    private const float MarginLogical = 16f;

    public static HudLayout ForMonitor(int monitorWidthPx, int monitorMarginRightPx, float dpiScale)
    {
        var widthPx = WidthLogical * dpiScale;
        var heightPx = HeightLogical * dpiScale;
        var marginPx = MarginLogical * dpiScale;
        var x = monitorWidthPx - widthPx - marginPx + monitorMarginRightPx;
        var y = marginPx;
        return new HudLayout(
            PositionPx: new Vector2(x, y),
            SizePx: new Vector2(widthPx, heightPx),
            DpiScale: dpiScale,
            Padding: HudPaddingPx.Default(dpiScale),
            FontSizes: HudFontSizesPx.Default(dpiScale)
        );
    }
}
