using System;

namespace Linerule.Platform.Windows;

/// <summary>
/// Static dispatch table for messages the <see cref="OverlayWindow"/>
/// WndProc has to forward to instance owners (which it cannot reference
/// directly because the WndProc is a class-level Win32 callback, not an
/// instance method). Each callback corresponds to one Win32 message that
/// the overlay HWND receives:
/// <list type="bullet">
///   <item><see cref="OnAppTick"/> — <c>WM_APP_TICK</c> (vsync pacer)</item>
///   <item><see cref="OnWmTimer"/> — <c>WM_TIMER</c> (HotkeyRepeater poll)</item>
/// </list>
/// One overlay per process, so static is the correct lifetime; setters
/// expose a deliberate hand-off point that <see cref="WindowsApp"/> /
/// <see cref="HotkeyRepeater"/> use during construction.
/// </summary>
internal static class OverlayWndProcDispatch
{
    public static Action? OnAppTick { get; set; }
    public static Action? OnWmTimer { get; set; }
}
