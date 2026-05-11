using Linerule.Core;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>Static helpers for monitor geometry queries.</summary>
public static class MonitorInfo
{
    /// <summary>
    /// Logical-pixel bounds of the primary monitor. Multi-monitor support is
    /// deferred to v0.2 (mirrors Rust ADR-0004).
    /// </summary>
    public static ScreenRect<Logical> PrimaryBounds(LoggerHandle log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var w = (uint)PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
        var h = (uint)PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);
        log.Debug("primary monitor bounds", new LogField("width", w), new LogField("height", h));
        return new ScreenRect<Logical>(new Point<Logical>(0, 0), w, h);
    }
}
