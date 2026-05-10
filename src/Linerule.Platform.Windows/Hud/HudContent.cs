using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Immutable HUD payload. Captures everything that gets painted in one
/// frame so equality comparison (via record) is the throttle: if
/// <c>oldContent == newContent</c>, skip re-rendering.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudContent(
    HudStatus Status,
    ImmutableArray<HudHotkeyRow> Hotkeys,
    HudTelemetry Telemetry,
    string? Hint
);
