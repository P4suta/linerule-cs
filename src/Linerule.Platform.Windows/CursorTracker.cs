using System;
using System.Runtime.InteropServices;
using System.Threading;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows;

/// <summary>
/// <c>GetCursorPos</c>-backed mouse tracker. Returns the cursor in logical
/// pixels, derived from the per-window DPI scale at construction.
/// Uses a hand-defined cursor-position struct + <c>DllImport</c> to avoid
/// CsWin32 metadata quirks where the Win32 <c>POINT</c> type sometimes fails
/// to be generated for this combination of TFM and metadata version.
/// </summary>
public sealed class CursorTracker(uint dpi) : IMouseTracker
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.CursorTracker);

    private readonly float _scale = dpi / 96f;
    private int _failureCount;

    public Point<Logical>? Poll()
    {
        if (!NativeBridge.GetCursorPos(out var raw))
        {
            // GetCursorPos failure is rare (would imply session not interactive)
            // — log every transition into failure but throttle steady-state spam.
            var n = Interlocked.Increment(ref _failureCount);
            if (n == 1 || n % 1000 == 0)
            {
                var (err, name) = Win32Guard.LastError();
                Log.Warn(
                    "GetCursorPos failed",
                    new LogField("count", n),
                    new LogField("err", err),
                    new LogField("err_name", name));
            }
            return null;
        }

        if (_failureCount > 0)
        {
            Log.Info("GetCursorPos recovered", new LogField("prior_failures", _failureCount));
            _failureCount = 0;
        }

        var logicalX = (int)Math.Round(raw.X / _scale);
        var logicalY = (int)Math.Round(raw.Y / _scale);
        return new Point<Logical>(logicalX, logicalY);
    }

    /// <summary>Win32 <c>POINT</c> ABI-compatible record struct (sequential layout, two ints).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CursorPosition(int X, int Y);

    private static class NativeBridge
    {
        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out CursorPosition lpPoint);
    }
}
