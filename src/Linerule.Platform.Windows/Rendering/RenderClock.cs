using System;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Render-loop driver, vsync-aligned via <c>DwmFlush()</c>. A dedicated
/// pacer thread blocks on <c>DwmFlush</c> (which returns at the next DWM
/// flip) and then posts <see cref="OverlayWindow.WM_APP_TICK"/> to the UI
/// thread. The UI thread's WndProc handler invokes the supplied
/// <see cref="onTick"/> action, mutates dcomp visuals, and calls
/// <c>IDCompositionDevice2.Commit()</c> — all single-threaded. The pacer
/// thread itself never touches dcomp state.
///
/// <para>
/// ADR-0010 Phase 2: <see cref="Compositor.RequestCommitAsync"/> is gone
/// along with WinAppSDK. <c>DwmFlush</c> is the simplest AOT-friendly
/// vsync source — synchronous, blocks until the next vblank, no callback
/// or await ceremony. Pacing accuracy is one frame at the display refresh
/// rate, identical to what RequestCommitAsync delivered.
/// </para>
///
/// <para>
/// <b>Stall protection</b>: <c>DwmFlush</c> can hang if DWM is paused
/// (RDP, locked session). The pacer thread loop checks the cancellation
/// token immediately after each DwmFlush returns; on cancel we exit the
/// loop and the dispose path joins the thread (bounded by one frame
/// since DwmFlush always returns within one display refresh).
/// </para>
/// </summary>
public sealed partial class RenderClock : IAsyncDisposable
{
    private readonly RenderBudget _budget;
    private readonly LoggerHandle _log;
    private readonly HWND _hwnd;
    private CancellationTokenSource? _cts;
    private Thread? _pacerThread;
    private bool _disposed;

    public RenderTiming Timing { get; }
    public RenderStats Stats { get; }

    public unsafe RenderClock(
        IntPtr overlayHwnd,
        RenderTiming timing,
        LoggerHandle log,
        double warnRatio = RenderBudget.DefaultWarnRatio
    )
    {
        _hwnd = new HWND(overlayHwnd.ToPointer());
        Timing = timing;
        Stats = new RenderStats();
        _log = log;
        _budget = new RenderBudget(timing, Stats, log, warnRatio);

        _log.Info(
            "RenderClock configured (dcomp + DwmFlush)",
            new LogField("display_hz", timing.DisplayRefreshHz),
            new LogField("frame_budget_ms", timing.FrameBudget.TotalMilliseconds)
        );
    }

    /// <summary>
    /// Start the vsync-paced loop. The pacer thread runs in the background
    /// and posts <see cref="OverlayWindow.WM_APP_TICK"/> after every DwmFlush
    /// return; the UI thread is responsible for actually invoking the tick
    /// callback (set on <see cref="OverlayWindow.OnAppTick"/>).
    /// Idempotent — second call is a no-op.
    /// </summary>
    public void Start()
    {
        if (_pacerThread is not null)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pacerThread = new Thread(() => PacerLoop(token)) { IsBackground = true, Name = "linerule-render-pacer" };
        _pacerThread.Start();
    }

    /// <summary>Request the loop stop. Await <see cref="DisposeAsync"/> to confirm completion.</summary>
    public void Stop() => _cts?.Cancel();

    /// <summary>
    /// Begin a tick budget window — invoked by the UI-thread WndProc handler
    /// before <c>onTick</c> runs. Pairs with <see cref="EndTick"/>.
    /// </summary>
    public void BeginTick() => _budget.Begin();

    /// <summary>End the tick budget window; emits warn telemetry if exceeded.</summary>
    public void EndTick() => _budget.End();

    private unsafe void PacerLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Blocks ~1 display refresh (16.67 ms at 60 Hz, 6.94 ms at
                // 144 Hz). Returns immediately if the compositor is paused
                // — that's still acceptable since we'll just spin a fast
                // post loop, and the cancellation check below catches us
                // on the next iteration.
                _ = PInvoke.DwmFlush();
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                // PostMessage is thread-safe and queues on the target HWND's
                // message queue — the UI thread will pump it on the next
                // GetMessage call.
                _ = PInvoke.PostMessage(_hwnd, OverlayWindow.WM_APP_TICK, default, default);
            }
        }
        catch (Exception ex)
        {
            _log.Error("render pacer thread crashed — overlay will stop updating", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var cts = _cts;
        var thread = _pacerThread;
        _cts = null;
        _pacerThread = null;

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }
        if (thread is not null)
        {
            // Bounded join: a wedged DwmFlush returns within one display
            // refresh, so 100 ms is generous. Run the join on the threadpool
            // to avoid blocking the awaiter (e.g. a UI thread).
            await Task.Run(
                    () =>
                    {
                        if (!thread.Join(TimeSpan.FromMilliseconds(100)))
                        {
                            _log.Warn("render pacer thread did not exit within 100 ms — abandoning");
                        }
                    },
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        cts?.Dispose();
    }
}
