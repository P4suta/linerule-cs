namespace Linerule.Config;

internal sealed record RawHudPadding(float? Edge, float? Section, float? Row, IReadOnlyList<string> UnknownKeys);
