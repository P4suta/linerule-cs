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
public sealed partial class CursorTracker(uint dpi) : IMouseTracker
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
                    new LogField("err_name", name)
                );
            }
            return null;
        }

        if (_failureCount > 0)
        {
            Log.Info("GetCursorPos recovered", new LogField("prior_failures", _failureCount));
            _failureCount = 0;
        }

        // GetCursorPos returns physical pixels on a per-monitor V2 process,
        // which is the same coordinate space as the HWND and the Composition
        // visual tree (MonitorInfo.PrimaryBounds + CreateWindowExW both
        // operate in physical pixels here). Passing the value through
        // unscaled keeps the rendered highlight aligned with the actual
        // pointer position (verified 2026-05-11: dividing by _scale at 150%
        // DPI offsets the bar ~1/3 of the screen to the left of the cursor).
        // The `Point<Logical>` phantom-type label is preserved for now —
        // the pipeline-wide rename to a more accurate marker is follow-up.
        _ = _scale; // keep the field for future hi-DPI heuristics.
        return new Point<Logical>(raw.X, raw.Y);
    }

    /// <summary>Win32 <c>POINT</c> ABI-compatible record struct (sequential layout, two ints).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CursorPosition(int X, int Y);

    private static partial class NativeBridge
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetCursorPos(out CursorPosition lpPoint);
    }
}
