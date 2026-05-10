using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Dispatching;
using Windows.UI.Composition;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// Top-most, click-through, true-per-pixel-alpha overlay built on the
/// WinAppSDK 2.x <see cref="ContentIsland"/> + <see cref="DesktopAttachedSiteBridge"/>
/// model paired with the OS-level <see cref="Windows.UI.Composition"/>
/// rendering surface.
/// <para>
/// HWND is created with <c>WS_EX_NOREDIRECTIONBITMAP</c> at <c>CreateWindowEx</c>
/// time (cannot be added later) so the surface goes straight to DComp without a
/// redirection bitmap — true per-pixel alpha, no color-key. See ADR-0009.
/// </para>
/// <para>
/// <b>TEMP click-through verification mode</b>: when investigating the
/// HTTRANSPARENT cross-process drop, <see cref="CreateHwnd"/> may swap to
/// <c>WS_EX_LAYERED + LWA_ALPHA</c> instead of <c>NOREDIRECTIONBITMAP</c>
/// to test the hypothesis that DWM's hit-test routing requires the
/// redirection surface. Removed once the architecture decision settles.
/// </para>
/// </summary>
public sealed class OverlayWindow : IOverlaySurface
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.OverlayWindow);
    private static readonly LoggerHandle WndProcLog = Logger.For(Subsystems.WndProc);

    private const string ClassName = "linerule-cs-overlay";

    private static WNDPROC? _wndProcKeepAlive;

    private readonly HWND _hwnd;
    private readonly Compositor _compositor;
    private readonly DesktopAttachedSiteBridge _bridge;
    private readonly ContentIsland _island;
    private readonly CompositionRenderer _renderer;
    private bool _disposed;

    public ScreenRect<Logical> MonitorBounds { get; }

    private OverlayWindow(
        HWND hwnd,
        Compositor compositor,
        DesktopAttachedSiteBridge bridge,
        ContentIsland island,
        ContainerVisual root,
        ScreenRect<Logical> monitor)
    {
        _hwnd = hwnd;
        _compositor = compositor;
        _bridge = bridge;
        _island = island;
        _renderer = new CompositionRenderer(compositor, root);
        MonitorBounds = monitor;
    }

    public static OverlayWindow Create(ScreenRect<Logical> monitor, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Log.Info("create begin",
            new("monitor_w", monitor.Width),
            new("monitor_h", monitor.Height));

        EnsureWindowClassRegistered();
        Log.Debug("window class registered", new("class", ClassName));

        var hwnd = CreateHwnd(monitor);
        Log.Info("CreateWindowExW ok", new("hwnd", $"0x{(nint)hwnd.Value:X}"));
        ExStyleSnapshot.Capture(hwnd, "after CreateWindowExW", Log);

        ApplyTempLayeredAlpha(hwnd);

        var (compositor, bridge, island, root) = AttachBridgeAndIsland(hwnd, queue);
        ExStyleSnapshot.Capture(hwnd, "after bridge.Connect", Log);

        Win32Guard.Check(
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE),
            "ShowWindow(SW_SHOWNOACTIVATE)",
            Log);
        Log.Info("ShowWindow ok — overlay live");
        ExStyleSnapshot.Capture(hwnd, "after ShowWindow", Log);

        return new OverlayWindow(hwnd, compositor, bridge, island, root, monitor);
    }

    /// <summary>
    /// TEMP: feeds the layered window a uniform alpha so the window is
    /// actually visible during click-through verification. No-op when the
    /// real <c>WS_EX_NOREDIRECTIONBITMAP</c> path is in use (which doesn't
    /// take SetLayeredWindowAttributes at all).
    /// </summary>
    private static unsafe void ApplyTempLayeredAlpha(HWND hwnd)
    {
        // 0xCC ≈ 80% opacity — uniform translucent grey across the monitor.
        Win32Guard.Check(
            PInvoke.SetLayeredWindowAttributes(
                hwnd,
                crKey: new COLORREF(0),
                bAlpha: 0xCC,
                dwFlags: LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA),
            "SetLayeredWindowAttributes(LWA_ALPHA=0xCC) [TEMP]",
            Log);
    }

    private static unsafe HWND CreateHwnd(ScreenRect<Logical> monitor)
    {
        // === TEMP DIAGNOSTIC: 5-minute click-through verification ===
        // Hypothesis: NOREDIRECTIONBITMAP path doesn't participate in DWM's
        // cross-process hit-test routing, so HTTRANSPARENT is silently
        // dropped. Swap to WS_EX_LAYERED + LWA_ALPHA — the proven
        // cross-process click-through pairing — and observe whether clicks
        // start landing on Notepad/Explorer beneath the overlay.
        var ex = WINDOW_EX_STYLE.WS_EX_LAYERED
            | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
            | WINDOW_EX_STYLE.WS_EX_TOPMOST;
        var style = WINDOW_STYLE.WS_POPUP;

        fixed (char* className = ClassName)
        fixed (char* title = "linerule")
        {
            return Win32Guard.CheckHandle(
                PInvoke.CreateWindowEx(
                    dwExStyle: ex,
                    lpClassName: className,
                    lpWindowName: title,
                    dwStyle: style,
                    X: monitor.Left,
                    Y: monitor.Top,
                    nWidth: (int)monitor.Width,
                    nHeight: (int)monitor.Height,
                    hWndParent: HWND.Null,
                    hMenu: default,
                    hInstance: PInvoke.GetModuleHandle(default(PCWSTR)),
                    lpParam: null),
                "CreateWindowExW overlay",
                Log);
        }
    }

    private static unsafe (Compositor Compositor, DesktopAttachedSiteBridge Bridge, ContentIsland Island, ContainerVisual Root)
        AttachBridgeAndIsland(HWND hwnd, DispatcherQueue queue)
    {
        var bridgeLog = Logger.For(Subsystems.Bridge);
        var compLog = Logger.For(Subsystems.Composition);

        var windowId = Win32Interop.GetWindowIdFromWindow((nint)hwnd.Value);
        bridgeLog.Debug("GetWindowIdFromWindow ok", new("id", $"0x{windowId.Value:X}"));

        SystemDispatcherQueue.EnsureForCurrentThread();
        compLog.Debug("Windows.System.DispatcherQueue established for thread");

        var compositor = new Compositor();
        compLog.Info("Windows.UI.Composition.Compositor created");

        var root = compositor.CreateContainerVisual();
        var island = ContentIsland.CreateForSystemVisual(queue, root);
        compLog.Info("ContentIsland.CreateForSystemVisual ok");

        var bridge = DesktopAttachedSiteBridge.CreateFromWindowId(queue, windowId);
        bridgeLog.Info("DesktopAttachedSiteBridge.CreateFromWindowId ok");

        bridge.ProcessesPointerInput = false;
        bridge.ProcessesKeyboardInput = false;
        bridgeLog.Info(
            "click-through knob set",
            new("processes_pointer_input", false),
            new("processes_keyboard_input", false));

        bridge.Connect(island);
        bridgeLog.Info("bridge.Connect(island) ok");

        return (compositor, bridge, island, root);
    }

    public void Apply(OverlayFrame frame) => _renderer.Apply(frame);

    public IntPtr Hwnd => _hwnd;

    public uint Dpi => PInvoke.GetDpiForWindow(_hwnd);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        Log.Info("dispose begin");
        _bridge.Dispose();
        _island.Dispose();
        _compositor.Dispose();
        Win32Guard.Check(PInvoke.DestroyWindow(_hwnd), "DestroyWindow overlay", Log);
        _disposed = true;
        Log.Info("dispose ok");
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Re-assert always-on-top z-order (called by ForegroundHook on
    /// <c>EVENT_SYSTEM_FOREGROUND</c>).
    /// </summary>
    public unsafe void ReassertTopmost()
    {
        Win32Guard.Check(
            PInvoke.SetWindowPos(
                _hwnd,
                new HWND((void*)(nint)(-1)), // HWND_TOPMOST
                X: 0,
                Y: 0,
                cx: 0,
                cy: 0,
                uFlags: SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                    | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE),
            "SetWindowPos(HWND_TOPMOST)",
            Log);
    }

    private static unsafe void EnsureWindowClassRegistered()
    {
        if (_wndProcKeepAlive is not null)
        {
            return;
        }

        _wndProcKeepAlive = OverlayWndProc;

        // hbrBackground left default (zero-init): with WS_EX_NOREDIRECTIONBITMAP
        // there's no GDI surface to paint anyway.
        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = default,
                lpfnWndProc = _wndProcKeepAlive,
                hInstance = PInvoke.GetModuleHandle(default(PCWSTR)),
                lpszClassName = className,
            };

            Win32Guard.CheckOrThrow(
                PInvoke.RegisterClassEx(in wc) != 0,
                "RegisterClassExW overlay",
                Log);
        }
    }

    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const int HTTRANSPARENT = -1;

    private static int _nchitCount;
    private static int _clickCount;

    /// <summary>
    /// WndProc body. Click-through stack:
    /// <list type="number">
    ///   <item><c>WS_EX_TRANSPARENT</c> on the HWND (cursor pass-through hint)</item>
    ///   <item><c>bridge.ProcessesPointerInput = false</c></item>
    ///   <item><c>WM_NCHITTEST → HTTRANSPARENT</c> (this handler)</item>
    /// </list>
    /// Diagnostic counters: <c>_nchitCount</c> increments on every NCHITTEST
    /// hit (logged at TRACE with sampling); <c>_clickCount</c> increments
    /// when a click leaks past HTTRANSPARENT (logged at WARN — should be 0
    /// in a working configuration).
    /// </summary>
    private static LRESULT OverlayWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == WM_NCHITTEST)
        {
            var n = Interlocked.Increment(ref _nchitCount);
            if (WndProcLog.IsEnabled(LogLevel.Trace) && (n <= 3 || n % 200 == 0))
            {
                WndProcLog.Trace("WM_NCHITTEST → HTTRANSPARENT", new("count", n));
            }
            return new LRESULT(HTTRANSPARENT);
        }
        if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
        {
            var n = Interlocked.Increment(ref _clickCount);
            WndProcLog.Warn(
                "click reached overlay (click-through failed)",
                new("msg", $"0x{msg:X4}"),
                new("count", n));
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

/// <summary>
/// Diagnostic snapshot of an HWND's extended style + child window list.
/// Pure helper — no mutable state, no PInvoke side-effects beyond reads.
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
        var ex = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        log.Debug(
            $"ex-style snapshot ({label})",
            new("ex_style", $"0x{ex:X8}"),
            new("transparent", (ex & (long)WINDOW_EX_STYLE.WS_EX_TRANSPARENT) != 0),
            new("noredir", (ex & (long)WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP) != 0),
            new("layered", (ex & (long)WINDOW_EX_STYLE.WS_EX_LAYERED) != 0));

        _enumChildCount = 0;
        _enumLabel = label;
        _enumLog = log;
        Win32Guard.Check(
            PInvoke.EnumChildWindows(hwnd, EnumChildCallback, default),
            $"EnumChildWindows ({label})",
            log);
        if (_enumChildCount == 0)
        {
            log.Debug($"child HWNDs ({label})", new("count", 0));
        }
    }

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
                new("hwnd", $"0x{(nint)child.Value:X}"),
                new("class", name));
        }
        return true;
    }
}
