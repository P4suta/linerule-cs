namespace Linerule.Config;

internal sealed record RawTapStepConfig(int? Thickness, int? Opacity, IReadOnlyList<string> UnknownKeys);
