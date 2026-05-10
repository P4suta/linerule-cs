using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows;

/// <summary>
/// Event-driven hook on <c>EVENT_SYSTEM_FOREGROUND</c>. Fires the supplied
/// callback only at the moment the OS foreground window changes — no polling,
/// idle-time CPU is zero. Used by the overlay to re-assert TOPMOST z-order
/// after Alt+Tab / focus changes without a heartbeat timer.
/// </summary>
public sealed partial class ForegroundHook : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.ForegroundHook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // _callback is a field — and MUST stay one — to anchor a GC root for the
    // function pointer the OS keeps. Demoting to a local would let the GC
    // collect the delegate while SetWinEventHook still has its address.
    private readonly WinEventProc _callback;
    private readonly IntPtr _hookHandle;
    private readonly Action _onForegroundChanged;
    private bool _disposed;

    public ForegroundHook(Action onForegroundChanged)
    {
        ArgumentNullException.ThrowIfNull(onForegroundChanged);
        _onForegroundChanged = onForegroundChanged;
        _callback = OnEvent;
        _hookHandle = NativeBridge.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            idProcess: 0,
            idThread: 0,
            dwFlags: WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
        );

        if (_hookHandle == IntPtr.Zero)
        {
            var (err, name) = Win32Guard.LastError();
            Log.Warn(
                "SetWinEventHook failed — topmost re-assertion disabled",
                new LogField("err", err),
                new LogField("err_name", name)
            );
        }
        else
        {
            Log.Debug(
                "SetWinEventHook ok",
                new LogField("hook", string.Create(CultureInfo.InvariantCulture, $"0x{_hookHandle:X}"))
            );
        }
    }

    /// <summary>
    /// Releases the OS hook. Modern .NET guidance avoids finalizers
    /// (Meziantou MA0055); the hook is process-bound, so even a missed
    /// <see cref="Dispose"/> is auto-cleaned at process exit. We accept
    /// CA2216 (no finalizer for an unmanaged-resource owner) on that basis.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookHandle != IntPtr.Zero && !NativeBridge.UnhookWinEvent(_hookHandle))
        {
            var (err, name) = Win32Guard.LastError();
            Log.Warn("UnhookWinEvent failed", new LogField("err", err), new LogField("err_name", name));
        }
        _disposed = true;
    }

    private void OnEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime
    )
    {
        try
        {
            _onForegroundChanged();
        }
        catch (Exception ex)
        {
            // The OS owns this thread — letting the exception escape kills
            // the message pump for the entire app.
            Log.Error("foreground callback threw", ex);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime
    );

    private static partial class NativeBridge
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags
        );

        [LibraryImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnhookWinEvent(IntPtr hWinEventHook);
    }
}
