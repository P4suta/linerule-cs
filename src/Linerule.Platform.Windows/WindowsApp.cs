using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Config;
using Linerule.Core;
using Linerule.Input;
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
    private static unsafe void EnsureSystemDispatcherQueue(LoggerHandle log)
    {
        var options = new DispatcherQueueOptions
        {
            dwSize = (uint)sizeof(DispatcherQueueOptions),
            threadType = DISPATCHERQUEUE_THREAD_TYPE.DQTYPE_THREAD_CURRENT,
            apartmentType = DISPATCHERQUEUE_THREAD_APARTMENTTYPE.DQTAT_COM_NONE,
        };
        PInvoke.CreateDispatcherQueueController(options, out var sysController);
        _systemDispatcherAnchor = sysController;
        log.Info(
            "Windows.System.DispatcherQueueController created",
            new LogField("rcw_type", _systemDispatcherAnchor?.GetType().Name ?? "null")
        );
    }

    public static async Task<int> RunAsync(UserConfig config, LoggerRoot logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        var log = logger.For(Subsystems.WindowsApp);
        log.Info(
            "RunAsync begin",
            new LogField("db", logger.DbPath),
            new LogField("run_id", logger.RunId.ToString("D"))
        );

        try
        {
            return await RunCoreAsync(config, logger, log, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Error("RunAsync caught", ex);
            CrashDump.Dump("WindowsApp.RunAsync caught", ex, terminating: false);
            return 1;
        }
        finally
        {
            log.Info("RunAsync exit");
        }
    }

    /// <summary>
    /// Single-place lifecycle setup with strict using / await-using ordering —
    /// see split helpers <see cref="PrepareDispatchAndMonitor"/> and
    /// <see cref="RunLoopAndShutdownAsync"/>.
    /// </summary>
    /// <remarks>
    /// Disposal contract: <see cref="OverlayWindow.DisposeAsync"/> and
    /// <see cref="HotkeyHost.DisposeAsync"/> are idempotent, so the explicit
    /// <see cref="ShutdownAsync"/> inside <see cref="RunLoopAndShutdownAsync"/>
    /// can dispose them before the implicit <c>await using</c> at scope exit;
    /// <c>await using</c> stays to honor CA2000 and the
    /// <see cref="OverlayWindow.Create"/>-throws path.
    /// Construction order: <see cref="HotkeyRepeater"/> after <see cref="TickLoop"/>
    /// so it can probe <see cref="TickLoop.WouldAffectState"/> (saturated boundary
    /// or Off mode ⇒ no polling); LIFO disposal unhooks the repeater before the
    /// host. <see cref="CrashDump.RegisterStateSnapshot"/> and
    /// <see cref="Heartbeat"/> share the single <see cref="TickLoop.Snapshot"/>
    /// source. The lifted <see cref="DispatcherQueueController"/> is kept rooted
    /// for the whole scope via <see cref="GC.KeepAlive(object)"/>; we deliberately
    /// never call <c>ShutdownQueueAsync</c> (see <see cref="ShutdownAsync"/>).
    /// </remarks>
    private static async Task<int> RunCoreAsync(
        UserConfig config,
        LoggerRoot logger,
        LoggerHandle log,
        CancellationToken cancellationToken
    )
    {
        using var sessionScope = logger.PushSession(Guid.NewGuid());
        using var bootstrap = WindowsAppRuntimeBootstrap.Initialize();
        log.Info("WindowsAppRuntime bootstrapped");
        var (controller, queue, monitor) = PrepareDispatchAndMonitor(logger, log);
        await using var overlay = OverlayWindow.Create(monitor, queue, logger);
        await using var hotkeys = HotkeyHost.Create(logger.For(Subsystems.HotkeyHost));
        log.Info("HotkeyHost created");
        var cursor = new CursorTracker(logger.For(Subsystems.CursorTracker));
        RegisterAll(hotkeys, config.Hotkeys, config.Input.TapStep, logger.For(Subsystems.Hotkey));
        log.Info("hotkeys registered", new LogField("hint", "press Ctrl+Alt+R to cycle, Ctrl+Alt+Q to quit"));
        using var foregroundHook = new ForegroundHook(overlay.ReassertTopmost, logger.For(Subsystems.ForegroundHook));
        await using var registration = cancellationToken.Register(queue.EnqueueEventLoopExit);
        var timing = RenderTiming.Probe(log, config.Render.FallbackRefreshHz);
        using var hud = CreateHud(overlay, config.Hud, logger.For(Subsystems.Hud));
        await using var loop = new TickLoop(
            overlay,
            hotkeys,
            cursor,
            queue,
            timing,
            hud,
            config.Hotkeys,
            config.Hud,
            config.Render.WarnRatio,
            logger.For(Subsystems.TickLoop),
            logger.For(Subsystems.Composition)
        );
        // Saturation oracle is supplied as a typed capability instead of a
        // raw callback, so the FSM in Linerule.Input has no implicit
        // coupling to TickLoop. The closure snapshots the live state and
        // delegates to the pure Reduce.
        var oracle = new ReduceOracle(loop.SnapshotState);
        using var repeater = new HotkeyRepeater(
            queue,
            hotkeys,
            oracle,
            config.Input.Repeat,
            logger.For(Subsystems.Hotkey)
        );
        loop.Start();
        CrashDump.RegisterStateSnapshot(loop.Snapshot);
        using var heartbeat = new Heartbeat(loop.Snapshot, logger.For(Subsystems.Heartbeat));
        await RunLoopAndShutdownAsync(queue, loop, hotkeys, overlay, log).ConfigureAwait(false);
        GC.KeepAlive(controller);
        return 0;
    }

    /// <summary>
    /// One-shot dispatcher + primary-monitor preparation. Bundles the
    /// artifacts the lifecycle needs before any <c>await using</c> chain
    /// begins: the lifted <see cref="DispatcherQueueController"/>, its
    /// <see cref="DispatcherQueue"/>, and the primary monitor bounds. The
    /// system queue is established first so <c>Windows.UI.Composition</c>
    /// sees a dispatcher. The caller is responsible for rooting the
    /// returned controller for the lifetime of the run (via
    /// <c>GC.KeepAlive</c>); we deliberately do not call
    /// <c>ShutdownQueueAsync</c> on it (see remarks on
    /// <see cref="ShutdownAsync"/>).
    /// </summary>
    private static (
        DispatcherQueueController Controller,
        DispatcherQueue Queue,
        ScreenRect<Logical> Monitor
    ) PrepareDispatchAndMonitor(LoggerRoot logger, LoggerHandle log)
    {
        EnsureSystemDispatcherQueue(log);
        var controller = DispatcherQueueController.CreateOnCurrentThread();
        var queue = controller.DispatcherQueue;
        log.Info("Microsoft.UI.Dispatching.DispatcherQueue created");
        var monitor = MonitorInfo.PrimaryBounds(logger.For(Subsystems.Win32));
        return (controller, queue, monitor);
    }

    /// <summary>
    /// Pumps the WinAppSDK event loop on the calling thread, then runs the
    /// ordered teardown. Receives the loop / hotkey / overlay references by
    /// value so the caller's using / await-using scope still owns their
    /// disposal — this helper merely sequences the
    /// explicit <see cref="ShutdownAsync"/> call between
    /// <see cref="DispatcherQueue.RunEventLoop"/> returning and the caller's
    /// LIFO scope exit.
    /// </summary>
    private static async Task RunLoopAndShutdownAsync(
        DispatcherQueue queue,
        TickLoop loop,
        HotkeyHost hotkeys,
        OverlayWindow overlay,
        LoggerHandle log
    )
    {
        log.Info("entering run loop");
        queue.RunEventLoop();
        log.Info("run loop exited");
        await ShutdownAsync(loop, hotkeys, overlay, log).ConfigureAwait(false);
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
    private static async Task ShutdownAsync(TickLoop loop, HotkeyHost hotkeys, OverlayWindow overlay, LoggerHandle log)
    {
        await ShutdownStep(
                "stop tick loop",
                () =>
                {
                    loop.Stop();
                    return Task.CompletedTask;
                },
                log
            )
            .ConfigureAwait(false);
        await ShutdownStep("dispose hotkeys", () => hotkeys.DisposeAsync().AsTask(), log).ConfigureAwait(false);
        await ShutdownStep("dispose overlay", () => overlay.DisposeAsync().AsTask(), log).ConfigureAwait(false);
        log.Info("shutdown: complete");

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
        // LoggerRoot disposal is owned by AppContext (composition root); the
        // event-loop layer no longer pulls down logging on its own.
    }

    /// <summary>
    /// One step of the ordered teardown — logs the step, runs it, and
    /// catches/logs any throw so a stuck disposer doesn't block later steps.
    /// </summary>
    private static async Task ShutdownStep(string label, Func<Task> action, LoggerHandle log)
    {
        log.Debug($"shutdown: {label}");
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Warn(
                $"shutdown: {label} threw",
                new LogField("ex", ex.GetType().Name),
                new LogField("msg", ex.Message)
            );
        }
    }

    private static void RegisterAll(HotkeyHost host, HotkeyMap map, TapStepConfig tapStep, LoggerHandle hotkeyLog)
    {
        foreach (var (chord, action) in BuildBindings(map, tapStep))
        {
            var parsed = ChordParser.Parse(chord);
            if (parsed is Result<ChordSpec, ChordError>.Ok ok)
            {
                var result = host.Register(ok.Value, action);
                if (result is Result<Unit, HotkeyError>.Err err)
                {
                    hotkeyLog.Warn(
                        "register failed",
                        new LogField("chord", chord),
                        new LogField("error", err.Error.ToString())
                    );
                }
            }
            else if (parsed is Result<ChordSpec, ChordError>.Err err)
            {
                hotkeyLog.Warn(
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
    private static HudVisual CreateHud(OverlayWindow overlay, HudConfig hudCfg, LoggerHandle hudLog)
    {
        ArgumentNullException.ThrowIfNull(hudCfg);
        var dpiScale = overlay.Dpi / 96f;
        // `MonitorBounds.Width` already comes from GetSystemMetrics on a
        // per-monitor V2 process — i.e. physical pixels of the primary
        // monitor, matching the HWND's pixel size. Multiplying by dpiScale
        // would double-scale and push the HUD off-screen
        // (verified 2026-05-11: HUD at offset_x=3366 on a 2560-wide HWND).
        var monitorWidthPx = (int)overlay.MonitorBounds.Width;
        var layout = HudLayout.ForMonitor(monitorWidthPx, monitorMarginRightPx: 0, dpiScale: dpiScale, hudCfg);
        var layer = overlay.CreateLayer();
        return new HudVisual(overlay.Compositor, layer, layout, hudCfg, hudLog);
    }

    private static IEnumerable<(string Chord, OverlayAction Action)> BuildBindings(HotkeyMap map, TapStepConfig tap)
    {
        // Tap step sizes from config. Held presses ignore these and shrink
        // to ±1 via HotkeyRepeater so a long press reads as "stepless".
        yield return (map.CycleMode, OverlayAction.CycleMode.Instance);
        // Tap = persistent toggle (the WM_HOTKEY firing flips state and
        // sticks). HotkeyRepeater detects long-press (≥ 250 ms) and
        // re-fires the same toggle on release, undoing the flip — so a
        // sustained hold becomes "peek-through-and-restore" without
        // changing the bound action (user feedback 2026-05-11).
        yield return (map.ToggleVisible, OverlayAction.ToggleVisible.Instance);
        yield return (map.Thicker, new OverlayAction.BumpThickness(+tap.Thickness));
        yield return (map.Thinner, new OverlayAction.BumpThickness(-tap.Thickness));
        yield return (map.MoreOpaque, new OverlayAction.BumpOpacity(+tap.Opacity));
        yield return (map.LessOpaque, new OverlayAction.BumpOpacity(-tap.Opacity));
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
        private readonly OverlayWindow _overlay;
        private readonly HotkeyHost _hotkeys;
        private readonly CursorTracker _cursor;
        private readonly DispatcherQueue _queue;
        private readonly RenderClock _clock;
        private readonly HudVisual _hud;
        private readonly HotkeyMap _hotkeyMap;
        private readonly HudConfig _hudCfg;
        private readonly LoggerHandle _log;
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
            HotkeyMap hotkeyMap,
            HudConfig hudCfg,
            double warnRatio,
            LoggerHandle tickLog,
            LoggerHandle compositionLog
        )
        {
            ArgumentNullException.ThrowIfNull(hudCfg);
            _overlay = overlay;
            _hotkeys = hotkeys;
            _cursor = cursor;
            _queue = queue;
            _clock = new RenderClock(overlay.Compositor, timing, OnTick, compositionLog, warnRatio);
            _hud = hud;
            _hotkeyMap = hotkeyMap;
            _hudCfg = hudCfg;
            _log = tickLog;
        }

        public void Start()
        {
            // Initial paint: cursor fallback to monitor center so the first
            // frame has something sensible even before the cursor has been
            // polled. Same idempotent shape as the per-tick pipeline emits.
            if (_state.Visible && _state.Mode != Mode.Off)
            {
                var pos =
                    _cursor.Poll()
                    ?? new Point<Logical>(
                        _overlay.MonitorBounds.Left + ((int)_overlay.MonitorBounds.Width / 2),
                        _overlay.MonitorBounds.Top + ((int)_overlay.MonitorBounds.Height / 2)
                    );
                _lastCursor = pos;
                Interlocked.Increment(ref _frameSeq);
                ApplyEffect(new TickEffect.DrawOverlay(_state.Mode, pos, _state.Config));
            }
            else
            {
                ApplyEffect(TickEffect.ClearOverlay.Instance);
            }
            ApplyEffect(new TickEffect.RefreshHud(_state));
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
        /// <summary>Snapshot accessor used by <see cref="ReduceOracle"/>; the oracle re-runs <c>Reduce</c> against this snapshot.</summary>
        public State SnapshotState() => _state;

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
            // Drain queue once; pure pipeline decides what to do.
            var drained = new List<OverlayAction>();
            while (_hotkeys.TryDequeue(out var action))
            {
                drained.Add(action);
            }

            var world = new TickWorld(_state, _lastCursor, Volatile.Read(ref _frameSeq), _lastHudRefreshAtMs);
            var input = new TickInput(Environment.TickCount64, _cursor.Poll(), drained);
            var (next, effects) = TickPipeline.Step(world, input, _hudCfg.TelemetryRefreshMs);

            _state = next.State;
            _lastCursor = next.LastCursor;
            Volatile.Write(ref _frameSeq, next.FrameSeq);
            _lastHudRefreshAtMs = next.LastHudRefreshAtMs;

            foreach (var effect in effects)
            {
                ApplyEffect(effect);
            }
        }

        private void ApplyEffect(TickEffect effect)
        {
            switch (effect)
            {
                case TickEffect.Exit:
                    _log.Info("Quit requested");
                    _queue.EnqueueEventLoopExit();
                    break;
                case TickEffect.DrawOverlay draw:
                    _overlay.Apply(Render.Frame(draw.Mode, draw.Cursor, _overlay.MonitorBounds, draw.Config));
                    break;
                case TickEffect.ClearOverlay:
                    _overlay.Apply(OverlayFrame.Empty);
                    break;
                case TickEffect.RefreshHud refresh:
                    var content = HudComposer.Compose(
                        refresh.State,
                        _hotkeyMap,
                        _clock.Stats.Snapshot(),
                        _clock.Timing
                    );
                    _hud.Update(content);
                    break;
                case TickEffect.SetHudOpacity setOp:
                    UpdateHudOpacity(setOp.Cursor);
                    break;
                case TickEffect.LogStateChanged logEvt:
                    _log.Debug(
                        "state changed",
                        new LogField("action", logEvt.Action.GetType().Name),
                        new LogField("mode", logEvt.Mode),
                        new LogField("visible", logEvt.Visible)
                    );
                    break;
                default:
                    // Exhaustive over the closed TickEffect coproduct in
                    // Linerule.Input — any new variant becomes a compile
                    // warning at the discriminant site, not silent fall-through.
                    break;
            }
        }

        /// <summary>
        /// Decay HUD opacity exponentially as the reading-ruler slit
        /// approaches the HUD panel. Pure compute lives in
        /// <see cref="HudFadeKernel.ComputeOpacity"/> (in <c>Linerule.Input</c>);
        /// this method only marshals the HUD layout into the kernel's
        /// <see cref="HudFadeKernel.HudBounds"/> and pushes the result to
        /// the HUD surface.
        /// </summary>
        private void UpdateHudOpacity(Point<Logical> cursor)
        {
            var layout = _hud.Layout;
            var bounds = new HudFadeKernel.HudBounds(
                Left: layout.PositionPx.X,
                Top: layout.PositionPx.Y,
                Right: layout.PositionPx.X + layout.SizePx.X,
                Bottom: layout.PositionPx.Y + layout.SizePx.Y
            );
            var opacity = HudFadeKernel.ComputeOpacity(_state, cursor, bounds, _hudCfg.FadeDecayPx);
            _hud.SetOpacity(opacity);
        }
    }
}
