using System;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Platform.Windows.Diagnostics;
using Windows.UI.Composition;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Render-loop driver, vsync-aligned via
/// <see cref="Compositor.RequestCommitAsync"/>. Each iteration waits for
/// the next compositor commit (which DWM paces at the display's vertical
/// blank), then samples + applies via the supplied <c>onTick</c> action.
/// The tick cadence therefore equals the display refresh rate by
/// construction — no separate timer, no clock drift, no per-frame phase
/// jitter.
///
/// <para>
/// <b>Historical context</b>: previous incarnations used a
/// <c>DispatcherQueueTimer</c> at 2× display refresh, then 1× with
/// implicit easing, then 1× with direct snap. All three suffered from
/// independent-clock drift against DWM vsync (the timer fires on the
/// dispatcher's own scheduler) and produced the frame-to-frame jitter
/// the user described as "stop-motion / jerky" (jp: <i>kakukaku</i> /
/// <i>gakugaku</i>). Hitching the loop onto the compositor's own commit
/// cycle is the structural fix — see ADR-0009 v3 + plan file
/// <c>repository-state-compressed-conway.md</c>.
/// </para>
///
/// <para>
/// <b>Threading</b>: the loop is started from the UI thread; <c>await</c>
/// on the projected <c>Task</c> resumes on the dispatcher's
/// SynchronizationContext, so <c>onTick</c> always runs on the UI thread
/// where Composition is bound. No marshaling code.
/// </para>
///
/// <para>
/// <b>Stall protection</b>: <see cref="Compositor.RequestCommitAsync"/>
/// can hang indefinitely if the compositor is paused (RDP, locked
/// session, minimized window). Each iteration races the commit
/// <see cref="Task"/> against a <c>2 × FrameBudget</c> delay; the timer
/// fires <see cref="RenderStats.RecordCommitTimeout"/> and lets the loop
/// continue so observability + HUD telemetry don't freeze with the
/// compositor.
/// </para>
/// </summary>
public sealed partial class RenderClock : IAsyncDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Composition);

    private readonly Compositor _compositor;
    private readonly RenderBudget _budget;
    private readonly Action _onTick;
    private readonly TimeSpan _commitTimeout;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;

    public RenderTiming Timing { get; }
    public RenderStats Stats { get; }

    public RenderClock(Compositor compositor, RenderTiming timing, Action onTick)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(onTick);
        _compositor = compositor;
        Timing = timing;
        Stats = new RenderStats();
        _budget = new RenderBudget(timing, Stats, Log);
        _onTick = onTick;
        // Two display frames is the practical ceiling: shorter and a single
        // slow commit on the GPU side false-positives; longer and a real
        // stall (RDP / minimized) freezes HUD telemetry too long.
        const int CommitTimeoutFrames = 2;
        _commitTimeout = TimeSpan.FromTicks(timing.FrameBudget.Ticks * CommitTimeoutFrames);

        Log.Info(
            "RenderClock configured",
            new LogField("display_hz", timing.DisplayRefreshHz),
            new LogField("frame_budget_ms", timing.FrameBudget.TotalMilliseconds),
            new LogField(Key: "commit_aligned", Value: true),
            new LogField("commit_timeout_ms", _commitTimeout.TotalMilliseconds)
        );
    }

    /// <summary>
    /// Start the vsync-aligned loop. Returns immediately; the loop runs
    /// on the UI thread via async <c>await</c>-continuation resumption.
    /// Safe to call once per <see cref="RenderClock"/> instance.
    /// </summary>
    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        _loopTask = RunAsync(_cts.Token);
    }

    /// <summary>
    /// Request the loop stop. The loop exits at the next iteration
    /// boundary; await <see cref="DisposeAsync"/> to confirm completion.
    /// </summary>
    public void Stop() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                var commit = _compositor.RequestCommitAsync().AsTask(ct);
                Task? timeout = null;
                try
                {
                    timeout = Task.Delay(_commitTimeout, ct);
                    var winner = await Task.WhenAny(commit, timeout).ConfigureAwait(true);
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    if (winner == timeout)
                    {
                        Stats.RecordCommitTimeout();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    // The Task.Delay loses its CT subscription when GC'd, but
                    // being explicit avoids accumulating cancellation registrations.
                    _ = timeout;
                }

                _budget.Begin();
                try
                {
                    _onTick();
                }
                catch (Exception ex)
                {
                    // Same swallow-and-log discipline as the old timer-tick path —
                    // a transient render error shouldn't kill the overlay session.
                    Log.Error("render tick threw", ex);
                }
                finally
                {
                    _budget.End();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — Stop() / DisposeAsync() cancelled us.
        }
        catch (Exception ex)
        {
            Log.Error("render loop crashed — overlay will stop updating", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Capture both lifetime handles into locals BEFORE doing any
        // async work — Stop() can be invoked re-entrantly during
        // shutdown and would otherwise see a half-cleared state. Nulling
        // the fields up-front makes the later operations effectively
        // owned by this call.
        var cts = _cts;
        var loopTask = _loopTask;
        _cts = null;
        _loopTask = null;

        if (cts is not null)
        {
            try
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by an earlier path — fine.
            }
        }
        if (loopTask is not null)
        {
            // Bounded wait — a wedged compositor mustn't block shutdown
            // forever. One second is generous; the loop typically exits
            // within one commit cycle (~16 ms at 60 Hz). Pass
            // CancellationToken.None deliberately: we ARE the cancellation,
            // so further interruption of the wait would risk leaking the
            // loop task.
            var winner = await Task.WhenAny(loopTask, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None))
                .ConfigureAwait(false);
            if (winner != loopTask)
            {
                Log.Warn("render loop did not exit within 1 s — abandoning task");
                // Attach a fault-observer so any post-shutdown exception
                // from the abandoned task is logged rather than landing
                // in TaskScheduler.UnobservedTaskException silently.
                _ = loopTask.ContinueWith(
                    static t =>
                    {
                        if (t.IsFaulted && t.Exception is { } ex)
                        {
                            Log.Error("abandoned render loop task threw post-shutdown", ex);
                        }
                    },
                    TaskScheduler.Default
                );
            }
        }
        cts?.Dispose();
    }
}
