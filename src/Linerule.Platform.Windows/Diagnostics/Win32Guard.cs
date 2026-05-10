using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// FFI guard layer. Every PInvoke that can fail (returns
/// <see cref="BOOL"/>, <see cref="HWND"/>, <c>HRESULT</c>) goes through
/// here so failure surfaces are uniform: a warn/error log entry with
/// <c>operation</c>, <c>last_error</c>, decoded symbolic name, OS
/// message, caller member, and line — never a silent <see cref="BOOL"/>
/// false that propagates as misbehavior 30 frames later.
///
/// <para>
/// <b>Usage convention</b>: every <c>PInvoke.XxxW(...)</c> call MUST be
/// wrapped:
/// </para>
///
/// <code>
/// // Boolean-returning Win32 — logs on failure, returns the BOOL.
/// if (!Win32Guard.Check(PInvoke.SetWindowPos(...), "SetWindowPos topmost", _log))
/// {
///     // recover, or just continue — the warn is already logged
/// }
///
/// // Mandatory-success Win32 — throws Win32Exception on failure.
/// Win32Guard.CheckOrThrow(PInvoke.RegisterClassEx(...) != 0,
///     "RegisterClassExW overlay", _log);
///
/// // HWND-returning — non-null = ok; null = throw.
/// var hwnd = Win32Guard.CheckHandle(PInvoke.CreateWindowEx(...),
///     "CreateWindowExW overlay", _log);
///
/// // HRESULT-returning (COM interop).
/// Win32Guard.CheckHr(comCall, "IDCompositionDevice::CreateTargetForHwnd", _log);
/// </code>
///
/// The xtask <c>strict-code</c> rule rejects raw <c>PInvoke.</c> calls
/// outside this file (escape hatch: a <c>// Win32Guard: explicitly
/// unguarded — &lt;reason&gt;</c> comment on the same line).
/// </summary>
internal static class Win32Guard
{
    /// <summary>
    /// Returns the BOOL; on <c>false</c>, emits a WARN entry with caller
    /// info + decoded last error.
    /// </summary>
    public static bool Check(
        BOOL ok,
        string operation,
        LoggerHandle log,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        if (ok)
        {
            return true;
        }
        var err = Marshal.GetLastWin32Error();
        log.Warn(
            "win32 failure",
            new LogField("op", operation),
            new LogField("err", err),
            new LogField("err_name", DecodeName(err)),
            new LogField("caller", $"{caller ?? "?"}:{line}"));
        return false;
    }

    /// <summary>
    /// Same as <see cref="Check"/> but throws <see cref="Win32Exception"/>
    /// on failure — for paths where there is no recovery (window class
    /// registration, hot-key registration, etc.).
    /// </summary>
    public static void CheckOrThrow(
        BOOL ok,
        string operation,
        LoggerHandle log,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        if (ok)
        {
            return;
        }
        var err = Marshal.GetLastWin32Error();
        var name = DecodeName(err);
        log.Error(
            "win32 fatal",
            ex: null,
            new LogField("op", operation),
            new LogField("err", err),
            new LogField("err_name", name),
            new LogField("caller", $"{caller ?? "?"}:{line}"));
        throw new Win32Exception(err, $"{operation}: {name} (0x{err:X8})");
    }

    /// <summary>
    /// Returns the HWND if non-null; throws <see cref="Win32Exception"/>
    /// on null with the captured last-error.
    /// </summary>
    public static HWND CheckHandle(
        HWND h,
        string operation,
        LoggerHandle log,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        if (h != HWND.Null)
        {
            return h;
        }
        var err = Marshal.GetLastWin32Error();
        var name = DecodeName(err);
        log.Error(
            "win32 handle null",
            ex: null,
            new LogField("op", operation),
            new LogField("err", err),
            new LogField("err_name", name),
            new LogField("caller", $"{caller ?? "?"}:{line}"));
        throw new Win32Exception(err, $"{operation}: returned NULL — {name} (0x{err:X8})");
    }

    /// <summary>
    /// HRESULT check — non-negative is success. Negative HRs throw the
    /// native exception via <see cref="Marshal.ThrowExceptionForHR(int)"/>.
    /// </summary>
    public static void CheckHr(
        int hr,
        string operation,
        LoggerHandle log,
        [CallerMemberName] string? caller = null,
        [CallerLineNumber] int line = 0)
    {
        if (hr >= 0)
        {
            return;
        }
        log.Error(
            "hresult failure",
            ex: null,
            new LogField("op", operation),
            new LogField("hr", $"0x{hr:X8}"),
            new LogField("caller", $"{caller ?? "?"}:{line}"));
        Marshal.ThrowExceptionForHR(hr);
    }

    /// <summary>
    /// Snapshot of the current <c>GetLastError</c> (as decoded name +
    /// numeric code) for ad-hoc logging from PInvoke wrappers that have
    /// custom recovery logic. Does not allocate when called outside an
    /// error path.
    /// </summary>
    public static (int Code, string Name) LastError()
    {
        var err = Marshal.GetLastWin32Error();
        return (err, DecodeName(err));
    }

    /// <summary>
    /// Decode the most common Win32 error codes to their symbolic name.
    /// For unknown codes, returns the OS-localized message via
    /// <see cref="Marshal.GetPInvokeErrorMessage(int)"/>, prefixed with
    /// <c>WIN32_</c>.
    /// </summary>
    public static string DecodeName(int err) => err switch
    {
        0 => "ERROR_SUCCESS",
        2 => "ERROR_FILE_NOT_FOUND",
        3 => "ERROR_PATH_NOT_FOUND",
        5 => "ERROR_ACCESS_DENIED",
        6 => "ERROR_INVALID_HANDLE",
        8 => "ERROR_NOT_ENOUGH_MEMORY",
        14 => "ERROR_OUTOFMEMORY",
        87 => "ERROR_INVALID_PARAMETER",
        120 => "ERROR_CALL_NOT_IMPLEMENTED",
        122 => "ERROR_INSUFFICIENT_BUFFER",
        126 => "ERROR_MOD_NOT_FOUND",
        127 => "ERROR_PROC_NOT_FOUND",
        183 => "ERROR_ALREADY_EXISTS",
        232 => "ERROR_NO_DATA",
        998 => "ERROR_NOACCESS",
        1004 => "ERROR_INVALID_FLAGS",
        1158 => "ERROR_NO_SYSTEM_RESOURCES",
        1400 => "ERROR_INVALID_WINDOW_HANDLE",
        1401 => "ERROR_INVALID_MENU_HANDLE",
        1402 => "ERROR_INVALID_CURSOR_HANDLE",
        1403 => "ERROR_INVALID_ACCEL_HANDLE",
        1404 => "ERROR_INVALID_HOOK_HANDLE",
        1407 => "ERROR_CANNOT_FIND_WND_CLASS",
        1408 => "ERROR_WINDOW_OF_OTHER_THREAD",
        1409 => "ERROR_HOTKEY_ALREADY_REGISTERED",
        1410 => "ERROR_CLASS_ALREADY_EXISTS",
        1411 => "ERROR_CLASS_DOES_NOT_EXIST",
        1412 => "ERROR_CLASS_HAS_WINDOWS",
        1413 => "ERROR_INVALID_INDEX",
        1419 => "ERROR_HOTKEY_NOT_REGISTERED",
        1422 => "ERROR_INVALID_GW_COMMAND",
        1437 => "ERROR_NO_SCROLLBARS",
        1444 => "ERROR_INVALID_THREAD_ID",
        _ => $"WIN32_{err} ({Marshal.GetPInvokeErrorMessage(err)?.Trim() ?? "(no message)"})",
    };
}
