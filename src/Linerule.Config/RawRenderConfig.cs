namespace Linerule.Config;

internal sealed record RawRenderConfig(double? WarnRatio, int? FallbackRefreshHz, IReadOnlyList<string> UnknownKeys);
