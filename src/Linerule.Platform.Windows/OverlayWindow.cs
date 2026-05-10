using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Content;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// Top-most, click-through, true-per-pixel-alpha overlay on the modern
/// WinAppSDK 2.x stack: <see cref="Microsoft.UI.Composition.Compositor"/> +
/// <see cref="ContentIsland"/> + <see cref="DesktopAttachedSiteBridge"/>.
///
/// <para>
/// <b>HWND ex-style</b>: <c>WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST</c>.
/// DWM uses the layered window's redirection bitmap as the composition target —
/// Composition renders into it and DWM composites with per-pixel alpha to the
/// desktop; <c>WS_EX_TRANSPARENT</c> routes <c>WM_NCHITTEST</c> + clicks through to
/// the window beneath. <see cref="SetLayeredWindowAttributes"/> /
/// <see cref="UpdateLayeredWindow"/> are NOT used — those are the legacy GDI
/// pathways. See ADR-0009 v2 for the reasoning + the empirical click-through
/// evidence (2026-05-11).
/// </para>
/// </summary>
public sealed class OverlayWindow : IOverlaySurface
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.OverlayWindow);
    private static readonly LoggerHandle WndProcLog = Logger.For(Subsystems.WndProc);

    private const string ClassName = "linerule-cs-overlay";

    private static WNDPROC? _wndProcKeepAlive;

    private readonly HWND _hwnd;
    private readonly DesktopAttachedSiteBridge _bridge;
    private readonly ContentIsland _island;
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
        DesktopAttachedSiteBridge bridge,
        ContentIsland island,
        ContainerVisual root,
        ScreenRect<Logical> monitor
    )
    {
        _hwnd = hwnd;
        Compositor = compositor;
        _bridge = bridge;
        _island = island;
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

        var (compositor, bridge, island, root) = AttachBridgeAndIsland(hwnd, queue);
        ExStyleSnapshot.Capture(hwnd, "after bridge.Connect", Log);

        // ShowWindow's BOOL return is the previous show state, not a success
        // flag — go through CheckLastError so we only warn on a real fault.
        _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Win32Guard.CheckLastError("ShowWindow(SW_SHOWNOACTIVATE)", Log);
        Log.Info("ShowWindow ok — overlay live");
        ExStyleSnapshot.Capture(hwnd, "after ShowWindow", Log);

        return new OverlayWindow(hwnd, compositor, bridge, island, root, monitor);
    }

    private static unsafe HWND CreateHwnd(ScreenRect<Logical> monitor)
    {
        // Modern click-through + per-pixel alpha pairing (ADR-0009 v2):
        //
        //   WS_EX_LAYERED       — DWM uses the redirection bitmap; Composition
        //                         renders into it; DWM composites per-pixel
        //                         alpha to the desktop. NO SetLayeredWindowAttributes
        //                         call (those are LWA_COLORKEY / LWA_ALPHA — the
        //                         legacy GDI pathway). NO UpdateLayeredWindow
        //                         (the same legacy pathway). DWM owns the surface.
        //   WS_EX_TRANSPARENT   — DWM routes WM_NCHITTEST + clicks through to the
        //                         window beneath. Verified empirically 2026-05-11.
        //   WS_EX_NOACTIVATE    — overlay never grabs focus on click anomalies.
        //   WS_EX_TOOLWINDOW    — no taskbar entry / Alt-Tab presence.
        //   WS_EX_TOPMOST       — stays above normal windows.
        //
        // Deliberately NOT included:
        //   WS_EX_NOREDIRECTIONBITMAP — would bypass DWM's redirection surface
        //                                and break BOTH per-pixel alpha
        //                                composition AND cross-process click-through.
        //                                See ADR-0009 v2 / commit history.
        const WINDOW_EX_STYLE ex =
            WINDOW_EX_STYLE.WS_EX_LAYERED
            | WINDOW_EX_STYLE.WS_EX_TRANSPARENT
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
        DesktopAttachedSiteBridge Bridge,
        ContentIsland Island,
        ContainerVisual Root
    ) AttachBridgeAndIsland(HWND hwnd, DispatcherQueue queue)
    {
        var bridgeLog = Logger.For(Subsystems.Bridge);
        var compLog = Logger.For(Subsystems.Composition);

        var windowId = Win32Interop.GetWindowIdFromWindow((nint)hwnd.Value);
        bridgeLog.Debug("GetWindowIdFromWindow ok", new LogField("id", $"0x{windowId.Value:X}"));

        // Modern WinAppSDK 2.x path (per docs 2026-04): Microsoft.UI.Composition
        // throughout. ContentIsland.Create takes a Microsoft.UI.Composition.Visual,
        // not the legacy Windows.UI.Composition variant. This unblocks Win2D
        // (also Microsoft.UI-based) so the HUD shares the same visual tree.
        var compositor = new Compositor();
        compLog.Info("Microsoft.UI.Composition.Compositor created");

        var root = compositor.CreateContainerVisual();
        // RelativeSizeAdjustment(1, 1) makes the root fill its parent (the island).
        // Without this, the root visual has zero size — `ContentIsland.Create`
        // accepts it but `bridge.Connect(island)` then wedges in native code
        // on a zero-size composition target. Cf. WindowsAppSDK-Samples
        // Islands/UXFrameworksOnIslands/LiftedFrame.cpp (the canonical
        // ContentIsland.Create wiring).
        root.RelativeSizeAdjustment = new System.Numerics.Vector2(1.0f, 1.0f);
        var island = ContentIsland.Create(root);
        compLog.Info("ContentIsland.Create ok");

        var bridge = DesktopAttachedSiteBridge.CreateFromWindowId(queue, windowId);
        bridgeLog.Info("DesktopAttachedSiteBridge.CreateFromWindowId ok");

        bridge.ProcessesPointerInput = false;
        bridge.ProcessesKeyboardInput = false;
        bridgeLog.Info(
            "click-through knob set",
            new LogField("processes_pointer_input", Value: false),
            new LogField("processes_keyboard_input", Value: false)
        );

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
