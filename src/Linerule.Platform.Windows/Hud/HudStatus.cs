using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>Live status — Mode / Visible / Opacity / Thickness.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudStatus(string Mode, bool Visible, int OpacityPercent, int ThicknessPx);
