namespace Linerule.Config;

internal sealed record RawInputConfig(
    RawTapStepConfig? TapStep,
    RawRepeatConfig? Repeat,
    IReadOnlyList<string> UnknownKeys
);
