using System.Runtime.InteropServices;
using Linerule.Config;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Font sizes for each text class in the HUD (physical px = logical × DPI scale).
/// Family names live in <see cref="HudFonts"/> and reach
/// <see cref="HudRenderer"/> via a separate path (DirectWrite consumes them at
/// text-format construction, not at layout time).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudFontSizesPx(float Title, float Status, float Body, float Telemetry)
{
    /// <summary>Project the size scalars from <see cref="HudFonts"/> onto a DPI scale.</summary>
    public static HudFontSizesPx From(HudFonts cfg, float dpiScale)
    {
        System.ArgumentNullException.ThrowIfNull(cfg);
        return new(
            Title: cfg.Title * dpiScale,
            Status: cfg.Status * dpiScale,
            Body: cfg.Body * dpiScale,
            Telemetry: cfg.Telemetry * dpiScale
        );
    }
}
