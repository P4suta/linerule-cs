namespace Linerule.Core;

/// <summary>
/// Hotkey long-press timing parameters. <see cref="InitialDelayMs"/> is the
/// grace window before any repeat fires; <see cref="LongPressThresholdMs"/>
/// distinguishes a tap from a hold for the mode-cycle / undo pattern;
/// <see cref="SlowRepeatIntervalMs"/> paces the steady-state cadence after
/// the accelerating ramp; <see cref="ReleasePollMs"/> is the input-thread
/// polling interval for release detection.
/// </summary>
public sealed record RepeatConfig(
    int InitialDelayMs,
    int LongPressThresholdMs,
    int SlowRepeatIntervalMs,
    int ReleasePollMs
)
{
    public static RepeatConfig Default { get; } =
        new(InitialDelayMs: 250, LongPressThresholdMs: 250, SlowRepeatIntervalMs: 400, ReleasePollMs: 50);
}
