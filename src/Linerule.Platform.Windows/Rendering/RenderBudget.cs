using System;
using System.Diagnostics;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Per-tick render-budget guard. Each tick the caller wraps its work
/// in a <see cref="Begin"/> / <see cref="End"/> pair; when the elapsed
/// time crosses <see cref="WarnRatio"/> × <see cref="RenderTiming.FrameBudget"/>
/// the overrun is reported to <see cref="RenderStats.RecordDrop"/> AND
/// emitted as a sampled WARN log entry.
///
/// <para>
/// Why sampled? On a temporarily-loaded system, frames can run hot for
/// dozens of consecutive ticks; emitting one log line per overrun would
/// drown the JSONL stream. We log the FIRST overrun in any quiet stretch
/// + every Nth overrun thereafter, so the user sees both the start of the
/// problem and the steady state.
/// </para>
/// </summary>
public sealed class RenderBudget
{
    /// <summary>
    /// Default warn ratio: log when a tick eats > 80% of the per-frame
    /// budget. The earlier 0.5 threshold was reached by routine GPU jitter
    /// (~9 ms on a 16.6 ms budget) and the WARN log read as alarming for
    /// what is actually a non-issue (user 2026-05-11 "ちょびちょび警告
    /// っぽいの怖い"). 0.8 leaves a ~3 ms margin to vsync — wide enough
    /// to flag actual risk, narrow enough to silence steady-state noise.
    /// </summary>
    public const double DefaultWarnRatio = 0.8;

    private const int OverrunSamplingPeriod = 60;

    private readonly LoggerHandle _log;
    private readonly RenderStats _stats;
    private readonly TimeSpan _warnThreshold;
    private readonly Stopwatch _stopwatch = new();
    private long _overrunStreak;

    public TimeSpan FrameBudget { get; }
    public double WarnRatio { get; }

    public RenderBudget(RenderTiming timing, RenderStats stats, LoggerHandle log, double warnRatio = DefaultWarnRatio)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warnRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(warnRatio, 1.0);
        FrameBudget = timing.FrameBudget;
        WarnRatio = warnRatio;
        _stats = stats;
        _log = log;
        _warnThreshold = TimeSpan.FromTicks((long)(timing.FrameBudget.Ticks * warnRatio));
    }

    /// <summary>Start measuring this tick. Idempotent — the stopwatch resets.</summary>
    public void Begin() => _stopwatch.Restart();

    /// <summary>Stop measuring; record sample; emit overrun warn if applicable.</summary>
    public void End()
    {
        _stopwatch.Stop();
        var elapsed = _stopwatch.Elapsed;
        _stats.Record(elapsed);
        if (elapsed <= _warnThreshold)
        {
            _overrunStreak = 0;
            return;
        }
        _stats.RecordDrop();
        var streak = ++_overrunStreak;
        if (streak == 1 || streak % OverrunSamplingPeriod == 0)
        {
            _log.Warn(
                "render budget overrun",
                new LogField("elapsed_ms", elapsed.TotalMilliseconds),
                new LogField("budget_ms", FrameBudget.TotalMilliseconds),
                new LogField("warn_threshold_ms", _warnThreshold.TotalMilliseconds),
                new LogField("streak", streak)
            );
        }
    }
}
