using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Diagnostic snapshot of an HWND's extended style + child window list.
/// Pure helper — no mutable state beyond the per-thread enum scratchpad,
/// no PInvoke side-effects beyond reads.
///
/// <para>
/// Used at every overlay-creation checkpoint
/// (<c>after CreateWindowExW</c> / <c>after bridge.Connect</c> /
/// <c>after ShowWindow</c>) to surface "did the framework strip a style
/// or inject a child HWND that intercepts our WndProc" — the question
/// that, on 2026-05-11, took 3 turns to answer because we couldn't see
/// it directly.
/// </para>
/// </summary>
internal static class ExStyleSnapshot
{
    [ThreadStatic]
    private static int _enumChildCount;

    [ThreadStatic]
    private static string? _enumLabel;

    [ThreadStatic]
    private static LoggerHandle? _enumLog;

    public static unsafe void Capture(HWND hwnd, string label, LoggerHandle log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var ex = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        log.Debug(
            $"ex-style snapshot ({label})",
            new LogField("ex_style", string.Create(CultureInfo.InvariantCulture, $"0x{ex:X8}")),
            new LogField("transparent", (ex & (long)WINDOW_EX_STYLE.WS_EX_TRANSPARENT) != 0),
            new LogField("noredir", (ex & (long)WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP) != 0),
            new LogField("layered", (ex & (long)WINDOW_EX_STYLE.WS_EX_LAYERED) != 0)
        );

        _enumChildCount = 0;
        _enumLabel = label;
        _enumLog = log;
        // EnumChildWindows BOOL return is documented as "not used" — it
        // returns FALSE when the callback stops enumeration or there are
        // no children. Real failures show in GetLastError.
        _ = PInvoke.EnumChildWindows(hwnd, &EnumChildCallback, default);
        Win32Guard.CheckLastError($"EnumChildWindows ({label})", log);
        // Always log the count rather than guarding on == 0 — keeps CA1508
        // away (it can't see the static mutation through the function-pointer
        // indirection) and gives the operator a hard total to compare against
        // the per-child Debug lines EnumChildCallback emits.
        log.Debug($"child HWNDs ({label})", new LogField("count", _enumChildCount));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static unsafe BOOL EnumChildCallback(HWND child, LPARAM _)
    {
        _enumChildCount++;
        Span<char> buf = stackalloc char[128];
        fixed (char* p = buf)
        {
            var n = PInvoke.GetClassName(child, p, buf.Length);
            var name = n > 0 ? new string(p, 0, n) : "<unknown>";
            _enumLog?.Debug(
                $"child HWND ({_enumLabel})",
                new LogField("hwnd", string.Create(CultureInfo.InvariantCulture, $"0x{(nint)child.Value:X}")),
                new LogField("class", name)
            );
        }
        return true;
    }
}
