using System.Runtime.InteropServices;
using System.Threading;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows;

/// <summary>
/// <c>GetCursorPos</c>-backed mouse tracker. Returns the cursor in HWND
/// pixel space — i.e. physical pixels for a per-monitor-V2 DPI-aware
/// process, which is what every downstream consumer (Composition visual
/// tree, HUD geometry, slit math) operates in. Uses a hand-defined
/// cursor-position struct + <c>LibraryImport</c> to avoid CsWin32
/// metadata quirks where the Win32 <c>POINT</c> type sometimes fails to
/// be generated for this combination of TFM and metadata version.
///
/// <para>
/// <b>Phantom type</b>: the return type is labeled
/// <see cref="Point{Logical}"/> for historical reasons; the value is in
/// fact in HWND pixel space (= physical pixels). Renaming the phantom
/// tag to <c>HwndPx</c> is a pipeline-wide follow-up.
/// </para>
/// </summary>
public sealed partial class CursorTracker : IMouseTracker
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.CursorTracker);

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
