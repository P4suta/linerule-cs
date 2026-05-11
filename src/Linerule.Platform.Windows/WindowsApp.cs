using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Hud;
using Linerule.Platform.Windows.Rendering;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.System.WinRT;

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

    // Process-lifetime anchor for the system DispatcherQueueController.
    // Releasing this would tear down the queue and Windows.UI.Composition
    // would lose its dispatcher. Holding as object so the COM RCW lives.
    private static object? _systemDispatcherAnchor;

    /// <summary>
    /// Set up a <c>Windows.System.DispatcherQueue</c> on the current thread
    /// via <c>coremessaging!CreateDispatcherQueueController</c>. Required by
    /// <c>new Windows.UI.Composition.Compositor()</c> (otherwise throws
    /// <c>RPC_E_WRONG_THREAD</c>). Independent of the
    /// <see cref="DispatcherQueueController"/> we set up afterwards for
    /// <see cref="DispatcherQueueTimer"/> usage — per WinAppSDK docs the
    /// lifted and system dispatcher queues coexist on the same thread.
    /// </summary>
    private static unsafe void EnsureSystemDispatcherQueue()
    {
        var options = new DispatcherQueueOptions
        {
            dwSize = (uint)sizeof(DispatcherQueueOptions),
            threadType = DISPATCHERQUEUE_THREAD_TYPE.DQTYPE_THREAD_CURRENT,
            apartmentType = DISPATCHERQUEUE_THREAD_APARTMENTTYPE.DQTAT_COM_NONE,
        };
        PInvoke.CreateDispatcherQueueController(options, out var sysController);
        _systemDispatcherAnchor = sysController;
        Log.Info(
            "Windows.System.DispatcherQueueController created",
            new LogField("rcw_type", _systemDispatcherAnchor?.GetType().Name ?? "null")
        );
    }

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

        EnsureSystemDispatcherQueue();

        var controller = DispatcherQueueController.CreateOnCurrentThread();
        var queue = controller.DispatcherQueue;
        Log.Info("Microsoft.UI.Dispatching.DispatcherQueue created");

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
        var cursor = new CursorTracker();

        RegisterAll(hotkeys, config.Hotkeys);
        Log.Info("hotkeys registered", new LogField("hint", "press Ctrl+Alt+R to cycle, Ctrl+Alt+Q to quit"));

        using var foregroundHook = new ForegroundHook(overlay.ReassertTopmost);
        await using var registration = cancellationToken.Register(queue.EnqueueEventLoopExit);
        var timing = RenderTiming.Probe(Log);
        using var hud = CreateHud(overlay);
        await using var loop = new TickLoop(overlay, hotkeys, cursor, queue, timing, hud, config.Hotkeys);
        // Long-press auto-repeat layer. Constructed AFTER the tick loop
        // so it can probe `loop.WouldAffectState` — when a held chord's
        // action would be a no-op (saturated boundary or Off mode), the
        // repeater stops polling instead of flooding the channel. LIFO
        // scope cleanup unhooks the repeater from the hotkey host
        // before the host disposes, so no dangling subscriber.
        using var repeater = new HotkeyRepeater(queue, hotkeys, loop.WouldAffectState);
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
        // `MonitorBounds.Width` already comes from GetSystemMetrics on a
        // per-monitor V2 process — i.e. physical pixels of the primary
        // monitor, matching the HWND's pixel size. Multiplying by dpiScale
        // would double-scale and push the HUD off-screen
        // (verified 2026-05-11: HUD at offset_x=3366 on a 2560-wide HWND).
        var monitorWidthPx = (int)overlay.MonitorBounds.Width;
        var layout = HudLayout.ForMonitor(monitorWidthPx, monitorMarginRightPx: 0, dpiScale: dpiScale);
        var layer = overlay.CreateLayer();
        return new HudVisual(overlay.Compositor, layer, layout);
    }

    private static IEnumerable<(string Chord, OverlayAction Action)> BuildBindings(HotkeyMap map)
    {
        // Tap step sizes — what one isolated press moves. Held presses
        // ignore these and shrink to ±1 via HotkeyRepeater so a long
        // press reads as "stepless" (user feedback 2026-05-11
        // "感覚としては無段階"). 8 is the sweet spot for one-tap nudges:
        // chunky enough to feel intentional, fine enough that repeated
        // taps aren't comically far apart.
        //   thickness: 1..1024 logical px (Thickness.MaxValue)
        //   opacity:   1..255 alpha
        const int ThicknessTapStep = 8;
        const int OpacityTapStep = 8;

        yield return (map.CycleMode, OverlayAction.CycleMode.Instance);
        // Tap = persistent toggle (the WM_HOTKEY firing flips state and
        // sticks). HotkeyRepeater detects long-press (≥ 250 ms) and
        // re-fires the same toggle on release, undoing the flip — so a
        // sustained hold becomes "peek-through-and-restore" without
        // changing the bound action (user feedback 2026-05-11).
        yield return (map.ToggleVisible, OverlayAction.ToggleVisible.Instance);
        yield return (map.Thicker, new OverlayAction.BumpThickness(+ThicknessTapStep));
        yield return (map.Thinner, new OverlayAction.BumpThickness(-ThicknessTapStep));
        yield return (map.MoreOpaque, new OverlayAction.BumpOpacity(+OpacityTapStep));
        yield return (map.LessOpaque, new OverlayAction.BumpOpacity(-OpacityTapStep));
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
    private sealed partial class TickLoop : IAsyncDisposable
    {
        // HUD telemetry (FPS / dropped frames / commit timeouts) is paced
        // by wall-clock elapsed, not by tick count — because under
        // vsync-aligned scheduling the tick rate equals the display
        // refresh, which can vary (60 / 120 / 144 / 165 / 240 Hz) and
        // even fluctuate at runtime. 200 ms is the human-perceptible
        // refresh cadence target.
        private const long HudTelemetryRefreshIntervalMs = 200;

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
        private long _lastHudRefreshAtMs;
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
            _clock = new RenderClock(overlay.Compositor, timing, OnTick);
            _hud = hud;
            _hotkeyMap = hotkeyMap;
        }

        public void Start()
        {
            ApplyFrame();
            RefreshHud();
            _lastHudRefreshAtMs = Environment.TickCount64;
            _clock.Start();
        }

        public void Stop() => _clock.Stop();

        /// <summary>
        /// Pure probe: would applying <paramref name="action"/> change
        /// any visible state right now? Used by <see cref="HotkeyRepeater"/>
        /// to stop its polling loop the moment a held chord reaches a
        /// saturating boundary (Thickness/Opacity at MAX or MIN, or the
        /// overlay in Off mode where bumps are intentionally no-op).
        /// Reads <c>_state</c> but does not mutate it; safe to call from
        /// the same UI thread that owns the tick loop.
        /// </summary>
        public bool WouldAffectState(OverlayAction action)
        {
            var (_, delta) = Reduce.Apply(_state, action);
            return delta.IsAny;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            await _clock.DisposeAsync().ConfigureAwait(false);
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
                new LogField("tick_p50_ms", stats.P50.TotalMilliseconds),
                new LogField("tick_p99_ms", stats.P99.TotalMilliseconds),
                new LogField("tick_max_ms", stats.Max.TotalMilliseconds),
                new LogField("frames_total", stats.TotalFrames),
                new LogField("frames_dropped", stats.DroppedFrames),
                new LogField("commit_timeouts", stats.CommitTimeouts),
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
                UpdateHudOpacity(now);
            }

            // Periodic telemetry refresh — no state change, but the FPS /
            // p99 / dropped-frames numbers in the HUD slowly drift. Paced
            // by wall-clock elapsed (not tick count) so the refresh
            // cadence is invariant under display-Hz changes. HUD's own
            // equality gate ensures we don't redraw if nothing moved.
            var nowMs = Environment.TickCount64;
            if (nowMs - _lastHudRefreshAtMs >= HudTelemetryRefreshIntervalMs)
            {
                _lastHudRefreshAtMs = nowMs;
                RefreshHud();
            }
        }

        private void RefreshHud()
        {
            var content = HudComposer.Compose(_state, _hotkeyMap, _clock.Stats.Snapshot(), _clock.Timing);
            _hud.Update(content);
        }

        /// <summary>
        /// Decay HUD opacity exponentially as the reading-ruler slit
        /// approaches the HUD panel: <c>opacity = 1 − exp(−distance / decay)</c>.
        /// Distance is the projected gap between the slit and the HUD
        /// rectangle along the slit's axis (Y for horizontal mode, X for
        /// vertical). Overlap → opacity 0; far → opacity ~1. Smooth in both
        /// directions, no perceptible flicker (per user request 2026-05-11).
        /// </summary>
        private void UpdateHudOpacity(Point<Logical> cursor)
        {
            // Decay length tunes how aggressively the HUD fades. ~120 logical
            // pixels means a clear "out of the way" range while keeping the
            // visible region wide enough that the HUD is fully opaque the
            // moment the user looks away from it.
            const float DecayPx = 120f;
            var distance = SlitToHudGap(cursor);
            var opacity = 1f - MathF.Exp(-distance / DecayPx);
            _hud.SetOpacity(opacity);
        }

        /// <summary>
        /// HWND-pixel gap between the reading-ruler slit and the HUD
        /// rectangle along the slit's axis (Y for horizontal mode, X for
        /// vertical). Returns <see cref="float.PositiveInfinity"/> when the
        /// overlay is hidden or in <see cref="Mode.Off"/> — the HUD is then
        /// fully opaque regardless of cursor position.
        /// </summary>
        private float SlitToHudGap(Point<Logical> cursor)
        {
            if (!_state.Visible)
            {
                return float.PositiveInfinity;
            }
            var thickness = _state.Config.Thickness.Value;
            var hudBounds = _hud.Layout;
            var hudLeft = hudBounds.PositionPx.X;
            var hudTop = hudBounds.PositionPx.Y;
            var hudRight = hudLeft + hudBounds.SizePx.X;
            var hudBottom = hudTop + hudBounds.SizePx.Y;
            return _state.Mode switch
            {
                Mode.Horizontal => AxisGap(cursor.Y - (thickness / 2f), cursor.Y + (thickness / 2f), hudTop, hudBottom),
                Mode.Vertical => AxisGap(cursor.X - (thickness / 2f), cursor.X + (thickness / 2f), hudLeft, hudRight),
                Mode.Off => float.PositiveInfinity,
                _ => throw new System.Diagnostics.UnreachableException("unknown overlay mode"),
            };
        }

        /// <summary>
        /// Non-negative gap between two 1-D intervals on the same axis;
        /// 0 when they overlap. <c>max(0, max(bLo-aHi, aLo-bHi))</c> is the
        /// branch-free form of "if either interval ends before the other
        /// starts, return the gap; else 0".
        /// </summary>
        private static float AxisGap(float aLo, float aHi, float bLo, float bHi) =>
            Math.Max(0f, Math.Max(bLo - aHi, aLo - bHi));

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
