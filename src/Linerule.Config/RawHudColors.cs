using Linerule.Core;

namespace Linerule.Config;

internal sealed record RawHudColors(
    Rgba? Background,
    Rgba? Foreground,
    Rgba? Subtle,
    Rgba? Accent,
    Rgba? Hint,
    Rgba? Divider,
    IReadOnlyList<string> UnknownKeys
);
