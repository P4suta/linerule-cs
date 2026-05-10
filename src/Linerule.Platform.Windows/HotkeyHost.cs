using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// Receives <c>WM_HOTKEY</c> on a hidden message-only window and forwards each
/// fire to a <see cref="Channel{T}"/>. The overlay AppWindow can stay
/// <c>WS_EX_TRANSPARENT | WS_EX_NOACTIVATE</c>; this hidden HWND owns the hotkey
/// queue.
/// </summary>
public sealed class HotkeyHost : IHotkeyHost
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.HotkeyHost);

    private const string ClassName = "linerule-cs-hotkey-host";

    private static WNDPROC? _wndProcKeepAlive;
    private static readonly Dictionary<nint, HotkeyHost> HostByHwnd = [];

    private readonly HWND _hwnd;
    private readonly Channel<OverlayAction> _channel = Channel.CreateUnbounded<OverlayAction>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }
    );
    private readonly Dictionary<int, OverlayAction> _idToAction = [];
    private readonly Dictionary<ChordSpec, int> _chordToId = [];
    private int _nextId = 1;
    private bool _disposed;

    private HotkeyHost(HWND hwnd) => _hwnd = hwnd;

    public static unsafe HotkeyHost Create()
    {
        EnsureWindowClassRegistered();

        // HWND_MESSAGE is the special parent-handle (-3) for message-only windows.
        var messageOnlyParent = new HWND((void*)(nint)(-3));

        fixed (char* className = ClassName)
        {
            fixed (char* title = "linerule-hotkey")
            {
                var hwnd = Win32Guard.CheckHandle(
                    PInvoke.CreateWindowEx(
                        dwExStyle: default,
                        lpClassName: className,
                        lpWindowName: title,
                        dwStyle: default,
                        X: 0,
                        Y: 0,
                        nWidth: 0,
                        nHeight: 0,
                        hWndParent: messageOnlyParent,
                        hMenu: default,
                        hInstance: PInvoke.GetModuleHandle(default(PCWSTR)),
                        lpParam: null
                    ),
                    "CreateWindowExW(HWND_MESSAGE) hotkey host",
                    Log
                );

                var host = new HotkeyHost(hwnd);
                HostByHwnd[(nint)hwnd.Value] = host;
                Log.Info(
                    "created",
                    new LogField("hwnd", string.Create(CultureInfo.InvariantCulture, $"0x{(nint)hwnd.Value:X}"))
                );
                return host;
            }
        }
    }

    public Result<Unit, HotkeyError> Register(ChordSpec chord, OverlayAction action)
    {
        ArgumentNullException.ThrowIfNull(chord);
        ArgumentNullException.ThrowIfNull(action);

        if (_chordToId.TryGetValue(chord, out var existingId))
        {
            Win32Guard.Check(PInvoke.UnregisterHotKey(_hwnd, existingId), "UnregisterHotKey (replace)", Log);
            _idToAction.Remove(existingId);
            _chordToId.Remove(chord);
        }

        var id = _nextId++;
        var mods = VirtualKey.FromModifiers(chord.Modifiers);
        var vk = VirtualKey.FromKeyCode(chord.Key);

        if (!PInvoke.RegisterHotKey(_hwnd, id, mods, vk))
        {
            var (err, name) = Win32Guard.LastError();
            Log.Warn(
                "RegisterHotKey failed",
                new LogField("id", id),
                new LogField("chord", chord.ToString() ?? "?"),
                new LogField("err", err),
                new LogField("err_name", name)
            );
            return Result.Err<Unit, HotkeyError>(new HotkeyError.OsRefused(chord, err));
        }

        _idToAction[id] = action;
        _chordToId[chord] = id;
        Log.Debug(
            "RegisterHotKey ok",
            new LogField("id", id),
            new LogField("chord", chord.ToString() ?? "?"),
            new LogField("action", action.GetType().Name)
        );
        return Result.Ok<Unit, HotkeyError>(Unit.Value);
    }

    public IAsyncEnumerable<OverlayAction> Subscribe(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Synchronous drain — used by the dispatcher-queue-driven main loop in <see cref="WindowsApp"/>.</summary>
    internal bool TryDequeue(out OverlayAction action) => _channel.Reader.TryRead(out action!);

    public unsafe ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        Log.Debug("dispose begin", new LogField("registered", _idToAction.Count));
        foreach (var id in _idToAction.Keys)
        {
            Win32Guard.Check(PInvoke.UnregisterHotKey(_hwnd, id), "UnregisterHotKey (dispose)", Log);
        }

        _channel.Writer.TryComplete();
        Win32Guard.Check(PInvoke.DestroyWindow(_hwnd), "DestroyWindow hotkey host", Log);
        HostByHwnd.Remove((nint)_hwnd.Value);
        _disposed = true;
        Log.Info("dispose ok");
        return ValueTask.CompletedTask;
    }

    private static unsafe void EnsureWindowClassRegistered()
    {
        if (_wndProcKeepAlive is not null)
        {
            return;
        }

        _wndProcKeepAlive = HotkeyWndProc;

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

            Win32Guard.CheckOrThrow(PInvoke.RegisterClassEx(in wc) != 0, "RegisterClassExW hotkey host", Log);
        }
    }

    private static unsafe LRESULT HotkeyWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == PInvoke.WM_HOTKEY && HostByHwnd.TryGetValue((nint)hwnd.Value, out var host))
        {
            var id = (int)(nint)wParam.Value;
            if (host._idToAction.TryGetValue(id, out var action))
            {
                _ = host._channel.Writer.TryWrite(action);
            }
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
