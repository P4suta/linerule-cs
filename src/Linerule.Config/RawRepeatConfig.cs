namespace Linerule.Config;

internal sealed record RawRepeatConfig(
    int? InitialDelayMs,
    int? LongPressThresholdMs,
    int? SlowRepeatIntervalMs,
    int? ReleasePollMs,
    IReadOnlyList<string> UnknownKeys
);
