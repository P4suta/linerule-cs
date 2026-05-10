namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Typed catalog of subsystem names — eliminates "string typo" failure
/// mode at log call sites and provides a single index of "what speaks
/// in this codebase."
/// </summary>
public static class Subsystems
{
    public const string WindowsApp = "WindowsApp";
    public const string OverlayWindow = "OverlayWindow";
    public const string TickLoop = "TickLoop";
    public const string HotkeyHost = "HotkeyHost";
    public const string Hotkey = "Hotkey";
    public const string Process = "Process";
    public const string AppDomainSink = "AppDomain";
    public const string TaskSchedulerSink = "TaskScheduler";
    public const string Win32 = "Win32";
    public const string Composition = "Composition";
    public const string Bridge = "Bridge";
    public const string WndProc = "WndProc";
    public const string Heartbeat = "Heartbeat";
    public const string CrashDump = "CrashDump";
    public const string ForegroundHook = "ForegroundHook";
    public const string CursorTracker = "CursorTracker";
    public const string Logger = "Logger";
    public const string Hud = "Hud";
}
