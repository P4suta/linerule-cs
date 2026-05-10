using System;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// First-class render-loop driver. Owns the <see cref="DispatcherQueueTimer"/>,
/// the <see cref="RenderTiming"/> derived from the active display, the
/// <see cref="RenderStats"/> rolling window, and the <see cref="RenderBudget"/>
/// guard. Callers attach an action via the constructor; the action runs
/// every <see cref="RenderTiming.TickInterval"/>.
///
/// <para>
/// This is the seam previously occupied by a magic-number
/// <c>_timer.Interval = TimeSpan.FromMilliseconds(8)</c>. Lifting it out
/// of <see cref="WindowsApp.TickLoop"/> means the display-refresh probe,
/// the per-tick budget logging, and the periodic stats emission all live
/// behind a single shape — and tests can construct one with synthetic
/// timing without spinning up a real DispatcherQueueTimer.
/// </para>
///
/// <para>
/// <b>Thread affinity</b>: Tick callback fires on the
/// <see cref="DispatcherQueueTimer"/>'s queue thread (the UI thread).
/// Render work runs there directly — no marshaling.
/// </para>
/// </summary>
public sealed partial class RenderClock : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Composition);

    private readonly DispatcherQueueTimer _timer;
    private readonly RenderBudget _budget;
    private readonly Action _onTick;
    private bool _disposed;

    public RenderTiming Timing { get; }
    public RenderStats Stats { get; }

    public RenderClock(DispatcherQueue queue, RenderTiming timing, Action onTick)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(onTick);
        Timing = timing;
        Stats = new RenderStats();
        _budget = new RenderBudget(timing, Stats, Log);
        _onTick = onTick;
        _timer = queue.CreateTimer();
        _timer.Interval = timing.TickInterval;
        _timer.Tick += OnTimerTick;

        Log.Info(
            "RenderClock configured",
            new LogField("display_hz", timing.DisplayRefreshHz),
            new LogField("tick_hz", timing.TickRateHz),
            new LogField("tick_interval_ms", timing.TickInterval.TotalMilliseconds),
            new LogField("frame_budget_ms", timing.FrameBudget.TotalMilliseconds)
        );
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void OnTimerTick(object? sender, object args)
    {
        if (_disposed)
        {
            return;
        }
        _budget.Begin();
        try
        {
            _onTick();
        }
        catch (Exception ex)
        {
            // The OS owns this thread; if the tick callback throws, the
            // dispatcher queue will surface the exception out of the run
            // loop. Log + swallow so a transient render error doesn't
            // crash the overlay session.
            Log.Error("render tick threw", ex);
        }
        finally
        {
            _budget.End();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
