using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// Top-most, click-through, true-per-pixel-alpha overlay built on
/// <b>DirectComposition direct</b> — <c>dcomp.dll</c>'s
/// <see cref="IDCompositionDesktopDevice"/> and friends — instead of
/// WinAppSDK's <c>Windows.UI.Composition.Compositor</c>. ADR-0010 Phase 2
/// retired the WinRT projection layer so the overlay can <c>&lt;PublishAot&gt;</c>.
///
/// <para>
/// <b>HWND ex-style</b>: <c>WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST</c>.
/// LAYERED and NOREDIRECTIONBITMAP coexist — LAYERED keeps DWM in the
/// click-routing path so <c>WS_EX_TRANSPARENT</c> can deliver cross-process
/// click-through, while NOREDIRECTIONBITMAP lets DirectComposition write
/// directly to its own GPU surface (no GDI redirection bitmap, full
/// per-pixel alpha). <c>SetLayeredWindowAttributes</c> and
/// <c>UpdateLayeredWindow</c> are deliberately NOT used — those are the
/// legacy GDI pathways (LWA_COLORKEY / LWA_ALPHA); DComposition owns the
/// surface and DWM composites it.
/// </para>
///
/// <para>
/// <b>Composition attachment</b>: <c>DCompositionCreateDevice2(d3d, iid, out)</c>
/// mints the desktop device.
/// <c>device.CreateTargetForHwnd(hwnd, isTopmost: false, out target)</c>
/// binds it to the overlay HWND. The root visual is then attached via
/// <c>target.SetRoot(root)</c>. No <c>ContainerVisual</c> /
/// <c>DesktopWindowTarget</c> WinRT types are touched. See ADR-0009 (dcomp-
/// direct amendment).
/// </para>
/// </summary>
public sealed class OverlayWindow : IOverlaySurface
{
    private const string ClassName = "linerule-cs-overlay";

    private static WNDPROC? _wndProcKeepAlive;

    /// <summary>
    /// WndProc log handle, assigned on the first <see cref="Create"/> call.
    /// The static Win32 callback <see cref="OverlayWndProc"/> has no instance
    /// reference (it serves the class, not a single HWND), so the logger has
    /// to live on a static field. Composition root threads it in once.
    /// </summary>
    private static LoggerHandle? _wndProcLog;

    private readonly LoggerHandle _log;
    private readonly HWND _hwnd;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual2 _root;
    private readonly IDCompositionVisual2 _backgroundLayer;
    private readonly IDCompositionVisual2 _foregroundLayer;
    private readonly CompositionRenderer _renderer;
    private bool _disposed;

    public ScreenRect<Logical> MonitorBounds { get; }

    /// <summary>The dcomp device hosting the overlay's visual tree.</summary>
    internal IDCompositionDesktopDevice Device { get; }

    /// <summary>
    /// Create a new top-level visual inside the overlay's foreground
    /// container. Foreground sits above the background container (where
    /// <see cref="CompositionRenderer"/> paints the mask + stripes), so
    /// layers handed out here are never dimmed by the focus-mode mask
    /// even when the mask covers the entire HWND. Each subsystem that
    /// wants its own layer (HUD, tooltips, etc.) calls this once and
    /// adds its visuals to the returned container.
    /// </summary>
    internal IDCompositionVisual2 CreateLayer()
    {
        Device.CreateVisual(out var layer);
        _foregroundLayer.AddVisual(layer, insertAbove: true, referenceVisual: null);
        return layer;
    }

    /// <summary>Hex render of an <see cref="HWND"/> for log fields.</summary>
    private static unsafe string HexHwnd(HWND h) => string.Create(CultureInfo.InvariantCulture, $"0x{(nint)h.Value:X}");

    private OverlayWindow(
        HWND hwnd,
        IDCompositionDesktopDevice device,
        IDCompositionTarget target,
        IDCompositionVisual2 root,
        IDCompositionVisual2 backgroundLayer,
        IDCompositionVisual2 foregroundLayer,
        CompositionRenderer renderer,
        ScreenRect<Logical> monitor,
        LoggerHandle log
    )
    {
        _hwnd = hwnd;
        _log = log;
        Device = device;
        _target = target;
        _root = root;
        _backgroundLayer = backgroundLayer;
        _foregroundLayer = foregroundLayer;
        _renderer = renderer;
        MonitorBounds = monitor;
    }

    internal static OverlayWindow Create(ScreenRect<Logical> monitor, ID3D11Device d3dDevice, LoggerRoot logger)
    {
        ArgumentNullException.ThrowIfNull(d3dDevice);
        ArgumentNullException.ThrowIfNull(logger);

        var log = logger.For(Subsystems.OverlayWindow);
        var wndProcLog = logger.For(Subsystems.WndProc);
        var compLog = logger.For(Subsystems.Composition);

        // Stash the WndProc log so the static OverlayWndProc callback can
        // emit through it without an instance pointer.
        _wndProcLog = wndProcLog;

        log.Info("create begin", new LogField("monitor_w", monitor.Width), new LogField("monitor_h", monitor.Height));

        EnsureWindowClassRegistered(log);
        log.Debug("window class registered", new LogField("class", ClassName));

        var hwnd = CreateHwnd(monitor, log);
        log.Info("CreateWindowExW ok", new LogField("hwnd", HexHwnd(hwnd)));
        ExStyleSnapshot.Capture(hwnd, "after CreateWindowExW", log);

        var (device, target, root, backgroundLayer, foregroundLayer, renderer) = AttachDcomp(hwnd, d3dDevice, compLog);
        ExStyleSnapshot.Capture(hwnd, "after dcomp attach", log);

        // ShowWindow's BOOL return is the previous show state, not a success
        // flag — go through CheckLastError so we only warn on a real fault.
        _ = PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        Win32Guard.CheckLastError("ShowWindow(SW_SHOWNOACTIVATE)", log);
        log.Info("ShowWindow ok — overlay live");
        ExStyleSnapshot.Capture(hwnd, "after ShowWindow", log);

        return new OverlayWindow(hwnd, device, target, root, backgroundLayer, foregroundLayer, renderer, monitor, log);
    }

    private static unsafe HWND CreateHwnd(ScreenRect<Logical> monitor, LoggerHandle log)
    {
        // ADR-0009 click-through + per-pixel alpha pairing — the ex-style
        // mix is unchanged from the Compositor-based v3; only the composition
        // pipeline below it dropped from WinRT to dcomp-direct.
        //
        //   WS_EX_LAYERED + WS_EX_TRANSPARENT: DWM-level click routing.
        //   WS_EX_NOREDIRECTIONBITMAP: dcomp owns the GPU surface, no GDI
        //                              redirection bitmap.
        //   WS_EX_NOACTIVATE: never grabs focus on click anomalies.
        //   WS_EX_TOOLWINDOW: no taskbar entry / Alt-Tab presence.
        //   WS_EX_TOPMOST: stays above normal windows.
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
                    log
                );
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2050:Correctness of COM interop cannot be guaranteed after trimming.",
        Justification = "ADR-0010: Platform.Windows is IsAotCompatible=false by design; dcomp COM interop is the boundary contract."
    )]
    private static (
        IDCompositionDesktopDevice Device,
        IDCompositionTarget Target,
        IDCompositionVisual2 Root,
        IDCompositionVisual2 BackgroundLayer,
        IDCompositionVisual2 ForegroundLayer,
        CompositionRenderer Renderer
    ) AttachDcomp(HWND hwnd, ID3D11Device d3dDevice, LoggerHandle compLog)
    {
        // 1. dcomp device, with the existing D3D11 device as the rendering
        //    device. Request IDCompositionDesktopDevice directly so we get
        //    CreateTargetForHwnd without a second QI.
        var desktopIid = typeof(IDCompositionDesktopDevice).GUID;
        PInvoke.DCompositionCreateDevice2(d3dDevice, in desktopIid, out var deviceObj).ThrowOnFailure();
        var device = (IDCompositionDesktopDevice)deviceObj!;
        compLog.Info("IDCompositionDesktopDevice created");

        // 2. Target bound to HWND (topmost: false — ex-style WS_EX_TOPMOST
        //    + SetWindowPos re-assertion in ReassertTopmost own that).
        device.CreateTargetForHwnd(hwnd, topmost: false, out var target);
        compLog.Info("IDCompositionTarget bound to HWND");

        // 3. Root + background/foreground container visuals.
        device.CreateVisual(out var root);
        target.SetRoot(root);

        device.CreateVisual(out var backgroundLayer);
        device.CreateVisual(out var foregroundLayer);

        // Both children sit at offset 0,0 with no transform — they cover the
        // full HWND by virtue of the surfaces their descendants attach.
        // insertAbove=true with referenceVisual=null = put at top of children.
        root.AddVisual(backgroundLayer, insertAbove: true, referenceVisual: null);
        root.AddVisual(foregroundLayer, insertAbove: true, referenceVisual: backgroundLayer);

        var renderer = new CompositionRenderer(device, backgroundLayer);

        // 4. First commit so the OS picks up the initial (empty) tree.
        device.Commit();
        compLog.Info("dcomp initial commit");

        return (device, target, root, backgroundLayer, foregroundLayer, renderer);
    }

    public void Apply(OverlayFrame frame) => _renderer.Apply(frame);

    /// <summary>Commit the visual tree to DWM. Called on UI thread after a tick.</summary>
    public void Commit() => Device.Commit();

    public IntPtr Hwnd => _hwnd;

    public uint Dpi => PInvoke.GetDpiForWindow(_hwnd);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _log.Info("dispose begin");
        // dcomp objects are COM RCWs — release in reverse order. Renderer
        // owns its per-layer surfaces; release before the device that minted them.
        _renderer.Dispose();
        Marshal.FinalReleaseComObject(_foregroundLayer);
        Marshal.FinalReleaseComObject(_backgroundLayer);
        Marshal.FinalReleaseComObject(_root);
        Marshal.FinalReleaseComObject(_target);
        Marshal.FinalReleaseComObject(Device);
        Win32Guard.Check(PInvoke.DestroyWindow(_hwnd), "DestroyWindow overlay", _log);
        _disposed = true;
        _log.Info("dispose ok");
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
            _log
        );
    }

    private static unsafe void EnsureWindowClassRegistered(LoggerHandle log)
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

            Win32Guard.CheckOrThrow(PInvoke.RegisterClassEx(in wc) != 0, "RegisterClassExW overlay", log);
        }
    }

    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_TIMER_MSG = 0x0113;

    /// <summary>
    /// Application-defined "vsync tick" message — posted by the
    /// <see cref="Rendering.RenderClock"/> pacer thread to drive
    /// <see cref="OnAppTick"/> on the UI thread after each DWM flip.
    /// Keep clear of the <c>WM_APP</c> base (0x8000) so a future
    /// component can mint additional messages without colliding.
    /// </summary>
    internal const uint WM_APP_TICK = 0x8001;

    private const int HTTRANSPARENT = -1;

    private static int _nchitCount;
    private static int _clickCount;

    /// <summary>
    /// WndProc body. Dispatches:
    /// <list type="bullet">
    ///   <item><c>WM_NCHITTEST → HTTRANSPARENT</c> (click-through, belt-and-braces with WS_EX_TRANSPARENT)</item>
    ///   <item><c>WM_APP_TICK → OnAppTick</c> (vsync-paced render tick from pacer thread)</item>
    ///   <item><c>WM_TIMER → OnWmTimer</c> (hotkey repeat polling timer)</item>
    ///   <item><c>WM_DESTROY → PostQuitMessage(0)</c> (clean shutdown of the message loop)</item>
    /// </list>
    /// </summary>
    private static LRESULT OverlayWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        var log = _wndProcLog;
        if (msg == WM_NCHITTEST)
        {
            var n = Interlocked.Increment(ref _nchitCount);
            if (log is { } l && l.IsEnabled(LogLevel.Trace) && (n <= 3 || n % 200 == 0))
            {
                l.Trace("WM_NCHITTEST → HTTRANSPARENT", new LogField("count", n));
            }
            return new LRESULT(HTTRANSPARENT);
        }
        if (msg == WM_APP_TICK)
        {
            OverlayWndProcDispatch.OnAppTick?.Invoke();
            return new LRESULT(0);
        }
        if (msg == WM_TIMER_MSG)
        {
            OverlayWndProcDispatch.OnWmTimer?.Invoke();
            return new LRESULT(0);
        }
        if (msg == WM_DESTROY)
        {
            PInvoke.PostQuitMessage(0);
            return new LRESULT(0);
        }
        if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var n = Interlocked.Increment(ref _clickCount);
            log?.Warn(
                "click reached overlay (click-through failed)",
                new LogField("msg", $"0x{msg:X4}"),
                new LogField("count", n)
            );
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
