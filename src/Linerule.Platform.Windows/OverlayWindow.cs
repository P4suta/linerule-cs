using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Microsoft.UI.Dispatching;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// Top-most, click-through, true-per-pixel-alpha overlay built on the
/// canonical PowerToys MouseHighlighter pattern:
/// <see cref="Windows.UI.Composition.Compositor"/> +
/// <see cref="ICompositorDesktopInterop"/> +
/// <see cref="DesktopWindowTarget"/>.
///
/// <para>
/// <b>HWND ex-style</b>: <c>WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST</c>.
/// LAYERED and NOREDIRECTIONBITMAP coexist — LAYERED keeps DWM in the
/// click-routing path so <c>WS_EX_TRANSPARENT</c> can deliver cross-process
/// click-through, while NOREDIRECTIONBITMAP lets DirectComposition write
/// directly to its own GPU surface (no GDI redirection bitmap, full
/// per-pixel alpha). <c>SetLayeredWindowAttributes</c> and
/// <c>UpdateLayeredWindow</c> are deliberately NOT used — those are the
/// legacy GDI pathways (LWA_COLORKEY / LWA_ALPHA); Composition owns the
/// surface and DWM composites it.
/// </para>
///
/// <para>
/// <b>Composition attachment</b>: we QI <see cref="ICompositorDesktopInterop"/>
/// off a fresh <c>Windows.UI.Composition.Compositor</c> and call
/// <c>CreateDesktopWindowTarget(hwnd, isTopmost: false, …)</c>. NO
/// <c>ContentIsland</c>, NO <c>DesktopAttachedSiteBridge</c>: that path
/// belongs to Microsoft.UI.Composition + WinUI 3 island hosting and
/// wedges natively on <c>WS_EX_LAYERED</c> HWNDs in WinAppSDK 2.0
/// (verified 2026-05-11). See ADR-0009 v3 for the empirical trail and
/// the canonical PowerToys reference.
/// </para>
/// </summary>
public sealed class OverlayWindow : IOverlaySurface
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.OverlayWindow);
    private static readonly LoggerHandle WndProcLog = Logger.For(Subsystems.WndProc);

    private const string ClassName = "linerule-cs-overlay";

    private static WNDPROC? _wndProcKeepAlive;

    private readonly HWND _hwnd;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly CompositionRenderer _renderer;
    private bool _disposed;

    public ScreenRect<Logical> MonitorBounds { get; }

    /// <summary>
    /// The Composition device the overlay renders into — exposed so
    /// auxiliary visuals (HUD, future telemetry layers, etc.) can build
    /// their own surfaces against the same compositor.
    /// </summary>
    public Compositor Compositor { get; }

    /// <summary>
    /// Create a new top-level Composition layer parented to the overlay's
    /// root visual. The new container is inserted at the top of the
    /// child Z-order so it draws above the mask. Each subsystem that wants
    /// its own layer (HUD, tooltips, etc.) calls this once and adds its
    /// visuals to the returned container.
    /// </summary>
    public ContainerVisual CreateLayer()
    {
        var layer = Compositor.CreateContainerVisual();
        _root.Children.InsertAtTop(layer);
        return layer;
    }

    /// <summary>Hex render of an <see cref="HWND"/> for log fields.</summary>
    private static unsafe string HexHwnd(HWND h) => string.Create(CultureInfo.InvariantCulture, $"0x{(nint)h.Value:X}");

    private OverlayWindow(
        HWND hwnd,
        Compositor compositor,
        DesktopWindowTarget target,
        ContainerVisual root,
        ScreenRect<Logical> monitor
    )
    {
        _hwnd = hwnd;
        Compositor = compositor;
        _target = target;
        _root = root;
        _renderer = new CompositionRenderer(compositor, root);
        MonitorBounds = monitor;
    }

    public static OverlayWindow Create(ScreenRect<Logical> monitor, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        Log.Info("create begin", new LogField("monitor_w", monitor.Width), new LogField("monitor_h", monitor.Height));

        EnsureWindowClassRegistered();
        Log.Debug("window class registered", new LogField("class", ClassName));

        var hwnd = CreateHwnd(monitor);
        Log.Info("CreateWindowExW ok", new LogField("hwnd", HexHwnd(hwnd)));
        ExStyleSnapshot.Capture(hwnd, "after CreateWindowExW", Log);

        var (compositor, target, root) = AttachDesktopWindowTarget(hwnd);
        ExStyleSnapshot.Capture(hwnd, "after CreateDesktopWindowTarget", Log);

        // ShowWindow's BOOL return is the previous show state, not a success
        // flag — go through CheckLastError so we only warn on a real fault.
        _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Win32Guard.CheckLastError("ShowWindow(SW_SHOWNOACTIVATE)", Log);
        Log.Info("ShowWindow ok — overlay live");
        ExStyleSnapshot.Capture(hwnd, "after ShowWindow", Log);

        return new OverlayWindow(hwnd, compositor, target, root, monitor);
    }

    private static unsafe HWND CreateHwnd(ScreenRect<Logical> monitor)
    {
        // Click-through + per-pixel alpha pairing (ADR-0009 v3, PowerToys
        // MouseHighlighter pattern):
        //
        //   WS_EX_LAYERED            — DWM is in the windowing path; routes
        //                              WM_NCHITTEST + clicks through to the
        //                              window beneath when paired with
        //                              WS_EX_TRANSPARENT. NO LWA_* call —
        //                              Composition writes the surface; the
        //                              legacy SetLayeredWindowAttributes
        //                              GDI pathway is deliberately unused.
        //   WS_EX_TRANSPARENT        — cross-process click-through hint.
        //   WS_EX_NOREDIRECTIONBITMAP — DComp draws directly to its own GPU
        //                              surface; DWM composites with per-pixel
        //                              alpha. No GDI redirection bitmap.
        //                              LAYERED and NOREDIRECTIONBITMAP are
        //                              NOT mutually exclusive — they ship
        //                              together in PowerToys; LAYERED owns
        //                              click routing, NOREDIRECTIONBITMAP
        //                              owns the rendering path.
        //   WS_EX_NOACTIVATE         — overlay never grabs focus on click anomalies.
        //   WS_EX_TOOLWINDOW         — no taskbar entry / Alt-Tab presence.
        //   WS_EX_TOPMOST            — stays above normal windows.
        const WINDOW_EX_STYLE ex =
            WINDOW_EX_STYLE.WS_EX_LAYERED
            | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP
            | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
            | WINDOW_EX_STYLE.WS_EX_TOPMOST;
        const WINDOW_STYLE style = WINDOW_STYLE.WS_POPUP;

        fixed (char* className = ClassName)
        {
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
                        lpParam: null
                    ),
                    "CreateWindowExW overlay",
                    Log
                );
            }
        }
    }

    private static unsafe (
        Compositor Compositor,
        DesktopWindowTarget Target,
        ContainerVisual Root
    ) AttachDesktopWindowTarget(HWND hwnd)
    {
        var compLog = Logger.For(Subsystems.Composition);

        // The DispatcherQueue established on this thread by
        // WindowsApp.RunCoreAsync (Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread)
        // satisfies Windows.UI.Composition.Compositor's CoreMessaging
        // prerequisite — both projections share the underlying queue.
        var compositor = new Compositor();
        compLog.Info("Windows.UI.Composition.Compositor created");

        var interop = (ICompositorDesktopInterop)(object)compositor;
        interop.CreateDesktopWindowTarget((nint)hwnd.Value, isTopmost: false, out var targetPtr);
        DesktopWindowTarget target;
        try
        {
            target = (DesktopWindowTarget)Marshal.GetObjectForIUnknown(targetPtr);
        }
        finally
        {
            Marshal.Release(targetPtr);
        }
        compLog.Info("DesktopWindowTarget bound to HWND");

        var root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = new System.Numerics.Vector2(1.0f, 1.0f);
        target.Root = root;
        compLog.Info("root visual attached");

        return (compositor, target, root);
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
        _target.Dispose();
        Compositor.Dispose();
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
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
            ),
            "SetWindowPos(HWND_TOPMOST)",
            Log
        );
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

            Win32Guard.CheckOrThrow(PInvoke.RegisterClassEx(in wc) != 0, "RegisterClassExW overlay", Log);
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
    ///   <item><c>WS_EX_LAYERED + WS_EX_TRANSPARENT</c> on the HWND (DWM-level routing)</item>
    ///   <item><c>WM_NCHITTEST → HTTRANSPARENT</c> (this handler — belt and braces)</item>
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
                WndProcLog.Trace("WM_NCHITTEST → HTTRANSPARENT", new LogField("count", n));
            }
            return new LRESULT(HTTRANSPARENT);
        }
        if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var n = Interlocked.Increment(ref _clickCount);
            WndProcLog.Warn(
                "click reached overlay (click-through failed)",
                new LogField("msg", $"0x{msg:X4}"),
                new LogField("count", n)
            );
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
