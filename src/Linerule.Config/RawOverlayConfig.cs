using Linerule.Core;

namespace Linerule.Config;

internal sealed record RawOverlayConfig(
    Rgba? MaskColor,
    int? Thickness,
    int? Opacity,
    IReadOnlyList<string> UnknownKeys
);
