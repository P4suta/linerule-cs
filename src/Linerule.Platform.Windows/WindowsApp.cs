using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Hud;
using Linerule.Platform.Windows.Rendering;
using Microsoft.UI.Dispatching;

namespace Linerule.Platform.Windows;

/// <summary>
/// One-stop run loop. Bootstraps the WinAppSDK <see cref="DispatcherQueueController"/>,
/// creates the overlay (<see cref="OverlayWindow"/>) + hotkey host, registers
/// chords from the resolved <see cref="UserConfig"/>, drives a 60 Hz cursor
/// follow + hotkey drain via <see cref="DispatcherQueueTimer"/>, then runs the
/// dispatcher's event loop until <see cref="OverlayAction.Quit"/> is observed.
/// </summary>
public static partial class WindowsApp
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
        // Both OverlayWindow.DisposeAsync and HotkeyHost.DisposeAsync are
        // idempotent (guarded by `_disposed`), so it is safe for the
        // explicit ShutdownAsync below to dispose them BEFORE the implicit
        // `await using` does so at scope end. We need the ordered shutdown
        // to drain the dispatcher queue properly, but we keep `await using`
        // so the analyzer's CA2000 (and the runtime invariant) still hold
        // on every code path — including `OverlayWindow.Create` throwing.
        await using var overlay = OverlayWindow.Create(monitor, queue);
        await using var hotkeys = HotkeyHost.Create();
        Log.Info("HotkeyHost created");
        var cursor = new CursorTracker(overlay.Dpi);

        RegisterAll(hotkeys, config.Hotkeys);
        Log.Info("hotkeys registered", new LogField("hint", "press Ctrl+Alt+R to cycle, Ctrl+Alt+Q to quit"));

        using var foregroundHook = new ForegroundHook(overlay.ReassertTopmost);
        await using var registration = cancellationToken.Register(queue.EnqueueEventLoopExit);
        var timing = RenderTiming.Probe(Log);
        using var hud = CreateHud(overlay);
        using var loop = new TickLoop(overlay, hotkeys, cursor, queue, timing, hud, config.Hotkeys);
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

        await ShutdownAsync(loop, hotkeys, overlay).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Explicit teardown sequence with per-step logging. Order matters:
    /// <list type="number">
    ///   <item>Stop tick timer (no more state mutations / repaints)</item>
    ///   <item>Dispose hotkey host (unregister chords + destroy hidden HWND)</item>
    ///   <item>Dispose overlay (release bridge / island / compositor / HWND)</item>
    /// </list>
    /// <para>
    /// We deliberately do NOT call <c>controller.ShutdownQueueAsync()</c>:
    /// when invoked from the queue's owner thread (we are; we just returned
    /// from <c>RunEventLoop</c> on it), it deadlocks because the
    /// continuation it schedules can never run on a queue that has already
    /// stopped pumping. The first user end-to-end run on 2026-05-11 spent
    /// 17-79 s wedged on that exact line. WindowsAppRuntimeBootstrap's
    /// Dispose (the surrounding <see langword="using"/>) tears down the SDK cleanly,
    /// and the OS reclaims the dispatcher queue when the process exits.
    /// </para>
    /// <para>
    /// Each step is wrapped in try/catch so a stuck disposer doesn't block
    /// later steps.
    /// </para>
    /// </summary>
    private static async Task ShutdownAsync(TickLoop loop, HotkeyHost hotkeys, OverlayWindow overlay)
    {
        await ShutdownStep(
                "stop tick loop",
                () =>
                {
                    loop.Stop();
                    return Task.CompletedTask;
                }
            )
            .ConfigureAwait(false);
        await ShutdownStep("dispose hotkeys", () => hotkeys.DisposeAsync().AsTask()).ConfigureAwait(false);
        await ShutdownStep("dispose overlay", () => overlay.DisposeAsync().AsTask()).ConfigureAwait(false);
        Log.Info("shutdown: complete");

        // Force-flush stdout/stderr/JSONL — the parent shell otherwise
        // sometimes doesn't see the final newlines and the prompt looks
        // hung even though the process has exited.
        try
        {
            Console.Out.Flush();
        }
        catch (IOException)
        { /* ignore */
        }
        try
        {
            Console.Error.Flush();
        }
        catch (IOException)
        { /* ignore */
        }
        Logger.Shutdown();
    }

    /// <summary>
    /// One step of the ordered teardown — logs the step, runs it, and
    /// catches/logs any throw so a stuck disposer doesn't block later steps.
    /// </summary>
    private static async Task ShutdownStep(string label, Func<Task> action)
    {
        Log.Debug($"shutdown: {label}");
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn(
                $"shutdown: {label} threw",
                new LogField("ex", ex.GetType().Name),
                new LogField("msg", ex.Message)
            );
        }
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

    /// <summary>
    /// Construct the HUD bound to the overlay's compositor + a fresh layer
    /// at the top of its visual tree. Returns a disposable that owns the
    /// HUD's surface / brush / visual / Win2D resources.
    /// </summary>
    private static HudVisual CreateHud(OverlayWindow overlay)
    {
        var dpiScale = overlay.Dpi / 96f;
        var monitorWidthPx = (int)(overlay.MonitorBounds.Width * dpiScale);
        var layout = HudLayout.ForMonitor(monitorWidthPx, monitorMarginRightPx: 0, dpiScale: dpiScale);
        var layer = overlay.CreateLayer();
        return new HudVisual(overlay.Compositor, layer, layout);
    }

    private static IEnumerable<(string Chord, OverlayAction Action)> BuildBindings(HotkeyMap map)
    {
        // Step sizes: were +4 each. User feedback 2026-05-11 — "spamming the
        // hotkey to reach max is exhausting." Scaled up to ≈4-8 presses to
        // span the full range:
        //   thickness: 1..512 logical px → step 16 (≈32 presses full sweep,
        //              ~6 from default 28 to 128)
        //   opacity:   1..255 alpha       → step 24 (≈11 presses full sweep,
        //              ~4 from default 0xAA=170 to max 0xFF=255)
        // Long-press auto-repeat is the proper fix; bigger discrete steps
        // tide us over until that lands.
        const int ThicknessStep = 16;
        const int OpacityStep = 24;

        yield return (map.CycleMode, OverlayAction.CycleMode.Instance);
        yield return (map.ToggleVisible, OverlayAction.ToggleVisible.Instance);
        yield return (map.Thicker, new OverlayAction.BumpThickness(+ThicknessStep));
        yield return (map.Thinner, new OverlayAction.BumpThickness(-ThicknessStep));
        yield return (map.MoreOpaque, new OverlayAction.BumpOpacity(+OpacityStep));
        yield return (map.LessOpaque, new OverlayAction.BumpOpacity(-OpacityStep));
        yield return (map.Quit, OverlayAction.Quit.Instance);
    }

    /// <summary>
    /// Per-tick state machine driver. Owns the mutable State + cursor cache
    /// + frame counter; ticks come from a <see cref="RenderClock"/> so the
    /// timing policy (display-refresh probe, frame budget, stats) is one
    /// concern away. Hotkey drain happens at the same cadence as the cursor
    /// poll — this is the right place for it because the hotkey host already
    /// queues asynchronously, so latency is "next tick" worst case.
    /// </summary>
    private sealed partial class TickLoop : IDisposable
    {
        // Refresh the HUD's telemetry section every Nth tick — once per
        // ~200 ms at 125 Hz tick rate. The status section is event-driven
        // (refreshed on state change) so this counter only paces the
        // "FPS / dropped frames" line.
        private const int HudTelemetryRefreshEvery = 25;

        private readonly OverlayWindow _overlay;
        private readonly HotkeyHost _hotkeys;
        private readonly CursorTracker _cursor;
        private readonly DispatcherQueue _queue;
        private readonly RenderClock _clock;
        private readonly HudVisual _hud;
        private readonly HotkeyMap _hotkeyMap;
        private State _state = State.Default;
        private Point<Logical>? _lastCursor;
        private long _frameSeq;
        private long _hudTelemetryTick;
        private bool _disposed;

        public TickLoop(
            OverlayWindow overlay,
            HotkeyHost hotkeys,
            CursorTracker cursor,
            DispatcherQueue queue,
            RenderTiming timing,
            HudVisual hud,
            HotkeyMap hotkeyMap
        )
        {
            _overlay = overlay;
            _hotkeys = hotkeys;
            _cursor = cursor;
            _queue = queue;
            _clock = new RenderClock(queue, timing, OnTick);
            _hud = hud;
            _hotkeyMap = hotkeyMap;
        }

        public void Start()
        {
            ApplyFrame();
            RefreshHud();
            _clock.Start();
        }

        public void Stop() => _clock.Stop();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _clock.Dispose();
        }

        /// <summary>
        /// Snapshot used both by <see cref="Heartbeat"/> (live observability)
        /// and by <see cref="CrashDump.RegisterStateSnapshot"/> (post-mortem).
        /// Returns one flat field-array — same schema in both sinks so a
        /// single <c>jq</c> query analyses live + dump output.
        /// </summary>
        public LogField[] Snapshot()
        {
            var stats = _clock.Stats.Snapshot();
            return
            [
                new LogField("mode", _state.Mode),
                new LogField("visible", _state.Visible),
                new LogField("cursor_x", _lastCursor?.X ?? 0),
                new LogField("cursor_y", _lastCursor?.Y ?? 0),
                new LogField("frame_seq", Volatile.Read(ref _frameSeq)),
                new LogField("display_hz", _clock.Timing.DisplayRefreshHz),
                new LogField("tick_hz", _clock.Timing.TickRateHz),
                new LogField("tick_p50_ms", stats.P50.TotalMilliseconds),
                new LogField("tick_p99_ms", stats.P99.TotalMilliseconds),
                new LogField("tick_max_ms", stats.Max.TotalMilliseconds),
                new LogField("frames_total", stats.TotalFrames),
                new LogField("frames_dropped", stats.DroppedFrames),
            ];
        }

        private void OnTick()
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
                    RefreshHud();
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

            // Periodic telemetry refresh — no state change, but the FPS /
            // p99 / dropped-frames numbers in the HUD slowly drift. HUD's
            // own equality gate ensures we don't redraw if nothing moved.
            if (++_hudTelemetryTick % HudTelemetryRefreshEvery == 0)
            {
                RefreshHud();
            }
        }

        private void RefreshHud()
        {
            var content = HudComposer.Compose(_state, _hotkeyMap, _clock.Stats.Snapshot(), _clock.Timing);
            _hud.Update(content);
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
