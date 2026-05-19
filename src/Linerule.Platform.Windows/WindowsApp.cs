using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Core;
using Linerule.Input;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Hud;
using Linerule.Platform.Windows.Rendering;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Linerule.Platform.Windows;

/// <summary>
/// One-stop run loop. ADR-0010 Phase 2: WinAppSDK / DispatcherQueue retired
/// in favor of a manual Win32 message pump and dcomp-direct rendering.
/// Bootstraps the shared D3D11 device, creates the overlay
/// (<see cref="OverlayWindow"/>) + hotkey host, registers chords from the
/// resolved <see cref="UserConfig"/>, drives a vsync-paced tick via
/// <see cref="RenderClock"/>'s DwmFlush thread + WM_APP_TICK, and runs
/// <c>GetMessage / TranslateMessage / DispatchMessage</c> until a
/// <see cref="OverlayAction.Quit"/> posts <c>WM_QUIT</c>.
/// </summary>
public static partial class WindowsApp
{
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
    /// Single-place lifecycle setup with strict using / await-using ordering.
    /// </summary>
    /// <remarks>
    /// Construction order: D3D11 device → overlay window (creates dcomp
    /// device + visuals) → hotkey host → cursor tracker → hotkey registration
    /// → foreground hook → render clock → HUD → tick loop → hotkey repeater.
    /// LIFO disposal at scope exit.
    /// </remarks>
    private static async Task<int> RunCoreAsync(
        UserConfig config,
        LoggerRoot logger,
        LoggerHandle log,
        CancellationToken cancellationToken
    )
    {
        using var sessionScope = logger.PushSession(Guid.NewGuid());
        var d3dDevice = D3D11Devices.CreateBgra(log);
        // d2dDevice is the rendering device for the dcomp surface chain — see
        // ADR-0009 v4 amendment for why ID3D11Device cannot stand in here.
        var d2dDevice = D3D11Devices.CreateD2DDevice(d3dDevice, log);
        var monitor = MonitorInfo.PrimaryBounds(logger.For(Subsystems.Win32));
        var timing = RenderTiming.Probe(log, config.Render.FallbackRefreshHz);

        await using var overlay = OverlayWindow.Create(monitor, d2dDevice, logger);
        await using var hotkeys = HotkeyHost.Create(logger.For(Subsystems.HotkeyHost));
        var cursor = new CursorTracker(logger.For(Subsystems.CursorTracker));
        RegisterAll(hotkeys, config.Hotkeys, config.Input.TapStep, logger.For(Subsystems.Hotkey));
        log.Info("hotkeys registered", new LogField("hint", "press Ctrl+Alt+R to cycle, Ctrl+Alt+Q to quit"));
        using var foregroundHook = new ForegroundHook(overlay.ReassertTopmost, logger.For(Subsystems.ForegroundHook));
        await using var registration = cancellationToken.Register(() => PInvoke.PostQuitMessage(0));

        await using var clock = new RenderClock(
            overlay.Hwnd,
            timing,
            logger.For(Subsystems.Composition),
            config.Render.WarnRatio
        );
        using var hud = CreateHud(overlay, config.Hud, logger.For(Subsystems.Hud));
        await using var loop = new TickLoop(
            overlay,
            hotkeys,
            cursor,
            timing,
            hud,
            config.Hotkeys,
            config.Hud,
            clock,
            logger.For(Subsystems.TickLoop)
        );
        var oracle = new ReduceOracle(loop.SnapshotState);
        using var repeater = new HotkeyRepeater(
            overlay.Hwnd,
            hotkeys,
            oracle,
            config.Input.Repeat,
            logger.For(Subsystems.Hotkey)
        );

        OverlayWndProcDispatch.OnAppTick = () => RunOneTick(clock, loop, overlay, log);
        loop.Start();
        clock.Start();
        CrashDump.RegisterStateSnapshot(loop.Snapshot);
        using var heartbeat = new Heartbeat(loop.Snapshot, logger.For(Subsystems.Heartbeat));

        await PumpMessagesAsync(log).ConfigureAwait(false);

        OverlayWndProcDispatch.OnAppTick = null;
        await ShutdownAsync(loop, hotkeys, overlay, log).ConfigureAwait(false);
        Marshal.FinalReleaseComObject(d2dDevice);
        Marshal.FinalReleaseComObject(d3dDevice);
        return 0;
    }

    /// <summary>
    /// Per-tick WndProc handler — UI-thread side of the dcomp render path.
    /// Frames the tick + commit in the <see cref="RenderBudget"/> window so
    /// over-budget frames surface as warn-level telemetry. Swallows
    /// exceptions so a transient tick failure doesn't kill the message loop.
    /// </summary>
    private static void RunOneTick(RenderClock clock, TickLoop loop, OverlayWindow overlay, LoggerHandle log)
    {
        clock.BeginTick();
        try
        {
            loop.OnTick();
            overlay.Commit();
        }
        catch (Exception ex)
        {
            log.Error("tick threw — frame skipped", ex);
        }
        finally
        {
            clock.EndTick();
        }
    }

    /// <summary>
    /// Standard Win32 GetMessage / TranslateMessage / DispatchMessage loop.
    /// Runs synchronously on the UI thread; returns when WM_QUIT is dequeued
    /// (via <c>PostQuitMessage(0)</c>). Wrapped in <see cref="Task.Run"/>
    /// only to give the caller an awaitable handle without blocking the
    /// surrounding async context — the pump itself is fully synchronous.
    /// </summary>
    private static unsafe Task PumpMessagesAsync(LoggerHandle log)
    {
        log.Info("entering Win32 message loop");
        // The pump must run on the same OS thread that owns the HWND.
        // RunCoreAsync is invoked synchronously from the bootstrap; this
        // method runs on that same thread by virtue of Task.FromResult
        // returning immediately after the loop exits.
        while (PInvoke.GetMessage(out MSG msg, default, 0, 0).Value > 0)
        {
            _ = PInvoke.TranslateMessage(in msg);
            _ = PInvoke.DispatchMessage(in msg);
        }
        log.Info("Win32 message loop exited (WM_QUIT)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Explicit teardown sequence with per-step logging. Order matters:
    /// <list type="number">
    ///   <item>Stop tick loop + render clock (no more state mutations / repaints / posted ticks)</item>
    ///   <item>Dispose hotkey host (unregister chords + destroy hidden HWND)</item>
    ///   <item>Dispose overlay (release dcomp device / target / visuals / HWND)</item>
    /// </list>
    /// Each step is wrapped in try/catch so a stuck disposer doesn't block later steps.
    /// </summary>
    private static async Task ShutdownAsync(TickLoop loop, HotkeyHost hotkeys, OverlayWindow overlay, LoggerHandle log)
    {
        _ = loop;
        await ShutdownStep("dispose hotkeys", () => hotkeys.DisposeAsync().AsTask(), log).ConfigureAwait(false);
        await ShutdownStep("dispose overlay", () => overlay.DisposeAsync().AsTask(), log).ConfigureAwait(false);
        log.Info("shutdown: complete");

        // Force-flush stdout/stderr so the parent shell sees final newlines.
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
    }

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
    /// Construct the HUD bound to the overlay's dcomp device + a fresh
    /// foreground layer at the top of its visual tree. Returns a disposable
    /// that owns the HUD's surface / brush / visual / D2D resources.
    /// </summary>
    private static HudVisual CreateHud(OverlayWindow overlay, HudConfig hudCfg, LoggerHandle hudLog)
    {
        ArgumentNullException.ThrowIfNull(hudCfg);
        var dpiScale = overlay.Dpi / 96f;
        var monitorWidthPx = (int)overlay.MonitorBounds.Width;
        var layout = HudLayout.ForMonitor(monitorWidthPx, monitorMarginRightPx: 0, dpiScale: dpiScale, hudCfg);
        var layer = overlay.CreateLayer();
        return new HudVisual(overlay.Device, layer, layout, hudCfg, hudLog);
    }

    private static IEnumerable<(string Chord, OverlayAction Action)> BuildBindings(HotkeyMap map, TapStepConfig tap)
    {
        yield return (map.CycleMode, OverlayAction.CycleMode.Instance);
        yield return (map.ToggleVisible, OverlayAction.ToggleVisible.Instance);
        yield return (map.Thicker, new OverlayAction.BumpThickness(+tap.Thickness));
        yield return (map.Thinner, new OverlayAction.BumpThickness(-tap.Thickness));
        yield return (map.MoreOpaque, new OverlayAction.BumpOpacity(+tap.Opacity));
        yield return (map.LessOpaque, new OverlayAction.BumpOpacity(-tap.Opacity));
        yield return (map.Quit, OverlayAction.Quit.Instance);
    }

    /// <summary>
    /// Per-tick state machine driver. Owns the mutable State + cursor cache
    /// + frame counter; ticks come from <see cref="OverlayWndProcDispatch.OnAppTick"/>
    /// (posted by <see cref="RenderClock"/>'s pacer thread + dispatched on
    /// the UI thread by the WndProc).
    /// </summary>
    internal sealed partial class TickLoop : IAsyncDisposable
    {
        private readonly OverlayWindow _overlay;
        private readonly HotkeyHost _hotkeys;
        private readonly CursorTracker _cursor;
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
            RenderTiming timing,
            HudVisual hud,
            HotkeyMap hotkeyMap,
            HudConfig hudCfg,
            RenderClock clock,
            LoggerHandle tickLog
        )
        {
            ArgumentNullException.ThrowIfNull(hudCfg);
            ArgumentNullException.ThrowIfNull(clock);
            _overlay = overlay;
            _hotkeys = hotkeys;
            _cursor = cursor;
            _clock = clock;
            _hud = hud;
            _hotkeyMap = hotkeyMap;
            _hudCfg = hudCfg;
            _log = tickLog;
            _ = timing; // RenderClock owns timing now; kept in signature for caller clarity.
        }

        public void Start()
        {
            // Initial paint: cursor fallback to monitor center so the first
            // frame has something sensible even before the cursor has been
            // polled.
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
        }

        public State SnapshotState() => _state;

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
            return ValueTask.CompletedTask;
        }

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

        /// <summary>
        /// Drive one tick. Invoked by <see cref="OverlayWndProcDispatch.OnAppTick"/>
        /// (set up in <see cref="RunCoreAsync"/>). Runs on the UI thread.
        /// </summary>
        internal void OnTick()
        {
            if (_disposed)
            {
                return;
            }
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
                case TickEffect.Quit:
                    _log.Info("Quit requested");
                    PInvoke.PostQuitMessage(0);
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
                    break;
            }
        }

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
