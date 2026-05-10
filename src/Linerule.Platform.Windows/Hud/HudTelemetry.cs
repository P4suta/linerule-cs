using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>Render telemetry — display refresh, tick stats, drop count.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudTelemetry(int DisplayHz, int TickHz, double TickP99Ms, long FramesDropped);
