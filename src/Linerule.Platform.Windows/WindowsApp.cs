using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Linerule.Platform.Windows;

/// <summary>
/// One-stop run loop. Bootstraps the WinAppSDK <see cref="DispatcherQueueController"/>,
/// creates the overlay (<see cref="OverlayWindow"/>) + hotkey host, registers
/// chords from the resolved <see cref="UserConfig"/>, drives a 60 Hz cursor
/// follow + hotkey drain via <see cref="DispatcherQueueTimer"/>, then runs the
/// dispatcher's event loop until <see cref="OverlayAction.Quit"/> is observed.
/// </summary>
public static class WindowsApp
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.WindowsApp);
    private static readonly LoggerHandle TickLog = Logger.For(Subsystems.TickLoop);
    private static readonly LoggerHandle HotkeyLog = Logger.For(Subsystems.Hotkey);

    public static async Task<int> RunAsync(UserConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        Log.Info(
            "RunAsync begin",
            new LogField("jsonl", Logger.LogPath),
            new LogField("run_id", Logger.RunId.ToString("D"))
        );

        try
        {
            return await RunCoreAsync(config, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("RunAsync caught", ex);
            CrashDump.Dump("WindowsApp.RunAsync caught", ex, terminating: false);
            return 1;
        }
        finally
        {
            Log.Info("RunAsync exit");
        }
    }

    private static async Task<int> RunCoreAsync(UserConfig config, CancellationToken cancellationToken)
    {
        using var sessionScope = Logger.PushSession(Guid.NewGuid());

        using var bootstrap = WindowsAppRuntimeBootstrap.Initialize();
        Log.Info("WindowsAppRuntime bootstrapped");

        var controller = DispatcherQueueController.CreateOnCurrentThread();
        var queue = controller.DispatcherQueue;
        Log.Info("DispatcherQueue created");

        var monitor = MonitorInfo.PrimaryBounds();
        await using var overlay = OverlayWindow.Create(monitor, queue);
        await using var hotkeys = HotkeyHost.Create();
        Log.Info("HotkeyHost created");
        var cursor = new CursorTracker(overlay.Dpi);

        RegisterAll(hotkeys, config.Hotkeys);
        Log.Info("hotkeys registered", new LogField("hint", "press Ctrl+Alt+R to cycle, Ctrl+Alt+Q to quit"));

        using var foregroundHook = new ForegroundHook(overlay.ReassertTopmost);
        await using var registration = cancellationToken.Register(queue.EnqueueEventLoopExit);
        var loop = new TickLoop(overlay, hotkeys, cursor, queue);
        loop.Start();

        // Hand the loop's state to CrashDump so post-mortem includes the
        // current Mode/Lifecycle/cursor in the dump. Heartbeat reads the
        // same snapshot — one source of truth for "what is the app doing
        // right now."
        CrashDump.RegisterStateSnapshot(loop.Snapshot);

        using var heartbeat = new Heartbeat(loop.Snapshot);

        Log.Info("entering run loop");
        queue.RunEventLoop();
        Log.Info("run loop exited");
        loop.Stop();
        await controller.ShutdownQueueAsync();
        return 0;
    }

    private static void RegisterAll(HotkeyHost host, HotkeyMap map)
    {
        foreach (var (chord, action) in BuildBindings(map))
        {
            var parsed = ChordParser.Parse(chord);
            if (parsed is Result<ChordSpec, ChordError>.Ok ok)
            {
                var result = host.Register(ok.Value, action);
                if (result is Result<Unit, HotkeyError>.Err err)
                {
                    HotkeyLog.Warn(
                        "register failed",
                        new LogField("chord", chord),
                        new LogField("error", err.Error.ToString())
                    );
                }
            }
            else if (parsed is Result<ChordSpec, ChordError>.Err err)
            {
                HotkeyLog.Warn(
                    "chord parse failed",
                    new LogField("chord", chord),
                    new LogField("error", err.Error.ToHumanString())
                );
            }
        }
    }

    private static IEnumerable<(string Chord, OverlayAction Action)> BuildBindings(HotkeyMap map)
    {
        yield return (map.CycleMode, OverlayAction.CycleMode.Instance);
        yield return (map.ToggleVisible, OverlayAction.ToggleVisible.Instance);
        yield return (map.Thicker, new OverlayAction.BumpThickness(+4));
        yield return (map.Thinner, new OverlayAction.BumpThickness(-4));
        yield return (map.MoreOpaque, new OverlayAction.BumpOpacity(+4));
        yield return (map.LessOpaque, new OverlayAction.BumpOpacity(-4));
        yield return (map.Quit, OverlayAction.Quit.Instance);
    }

    /// <summary>
    /// Per-tick state machine driver. Encapsulates the mutable State + cursor cache so
    /// <see cref="WindowsApp.RunAsync"/> stays under MA0051's 60-line method ceiling.
    /// </summary>
    private sealed class TickLoop
    {
        private readonly OverlayWindow _overlay;
        private readonly HotkeyHost _hotkeys;
        private readonly CursorTracker _cursor;
        private readonly DispatcherQueue _queue;
        private readonly DispatcherQueueTimer _timer;
        private State _state = State.Default;
        private Point<Logical>? _lastCursor;
        private long _frameSeq;

        public TickLoop(OverlayWindow overlay, HotkeyHost hotkeys, CursorTracker cursor, DispatcherQueue queue)
        {
            _overlay = overlay;
            _hotkeys = hotkeys;
            _cursor = cursor;
            _queue = queue;
            _timer = queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            ApplyFrame();
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        /// <summary>
        /// Snapshot used both by <see cref="Heartbeat"/> (live observability)
        /// and by <see cref="CrashDump.RegisterStateSnapshot"/> (post-mortem).
        /// Returning a single flat field-array keeps the schema identical
        /// across both sinks so a downstream <c>jq</c> query works on both.
        /// </summary>
        public LogField[] Snapshot() =>
            [
                new LogField("mode", _state.Mode),
                new LogField("visible", _state.Visible),
                new LogField("cursor_x", _lastCursor?.X ?? 0),
                new LogField("cursor_y", _lastCursor?.Y ?? 0),
                new LogField("frame_seq", Volatile.Read(ref _frameSeq)),
            ];

        private void OnTick(object? sender, object args)
        {
            while (_hotkeys.TryDequeue(out var action))
            {
                if (action is OverlayAction.Quit)
                {
                    TickLog.Info("Quit requested");
                    _queue.EnqueueEventLoopExit();
                    return;
                }

                var (next, delta) = Reduce.Apply(_state, action);
                _state = next;
                if (delta.IsAny)
                {
                    TickLog.Debug(
                        "state changed",
                        new LogField("action", action.GetType().Name),
                        new LogField("mode", _state.Mode),
                        new LogField("visible", _state.Visible)
                    );
                    ApplyFrame();
                }
            }

            var current = _cursor.Poll();
            if (current is { } now && now != _lastCursor)
            {
                _lastCursor = now;
                if (_state.Visible && _state.Mode != Mode.Off)
                {
                    Interlocked.Increment(ref _frameSeq);
                    _overlay.Apply(Render.Frame(_state.Mode, now, _overlay.MonitorBounds, _state.Config));
                }
            }
        }

        private void ApplyFrame()
        {
            if (!_state.Visible || _state.Mode == Mode.Off)
            {
                _overlay.Apply(OverlayFrame.Empty);
                return;
            }

            var pos =
                _cursor.Poll()
                ?? new Point<Logical>(
                    _overlay.MonitorBounds.Left + ((int)_overlay.MonitorBounds.Width / 2),
                    _overlay.MonitorBounds.Top + ((int)_overlay.MonitorBounds.Height / 2)
                );
            _lastCursor = pos;
            Interlocked.Increment(ref _frameSeq);
            _overlay.Apply(Render.Frame(_state.Mode, pos, _overlay.MonitorBounds, _state.Config));
        }
    }
}
