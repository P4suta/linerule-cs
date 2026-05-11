using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Render telemetry shown in the HUD footer. <c>TickHz</c> dropped after
/// the move to vsync-aligned scheduling (the tick cadence equals
/// <c>DisplayHz</c> by construction); <c>CommitTimeouts</c> replaces it
/// as the new "is the render loop healthy?" signal — non-zero means the
/// compositor stalled (RDP / locked session / minimized).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudTelemetry(int DisplayHz, double TickP99Ms, long FramesDropped, long CommitTimeouts);
