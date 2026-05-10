using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Hud;

/// <summary>One row of the hotkey legend: chord on the left, action label on the right.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct HudHotkeyRow(string Chord, string Label);
