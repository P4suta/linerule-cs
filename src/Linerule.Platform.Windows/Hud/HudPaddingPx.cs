using System.Runtime.InteropServices;
using Linerule.Config;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Inner padding around the HUD content (physical px = logical × DPI scale).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudPaddingPx(float Edge, float Section, float Row)
{
    /// <summary>Project a <see cref="HudPadding"/> (logical px) onto a DPI scale.</summary>
    public static HudPaddingPx From(HudPadding cfg, float dpiScale)
    {
        System.ArgumentNullException.ThrowIfNull(cfg);
        return new(Edge: cfg.Edge * dpiScale, Section: cfg.Section * dpiScale, Row: cfg.Row * dpiScale);
    }
}
