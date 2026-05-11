using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// HUD theme colors. Stored as straight (non-premultiplied) RGBA;
/// <c>HudRenderer</c> applies premultiplication at the D2D brush boundary.
/// </summary>
public sealed record HudColors(Rgba Background, Rgba Foreground, Rgba Subtle, Rgba Accent, Rgba Hint, Rgba Divider)
{
    public static HudColors Default { get; } =
        new(
            Background: new Rgba(0x10, 0x12, 0x18, 0xD8),
            Foreground: new Rgba(0xE6, 0xE9, 0xEF, 0xFF),
            Subtle: new Rgba(0x9A, 0xA3, 0xB2, 0xFF),
            Accent: new Rgba(0xFF, 0xC2, 0x4D, 0xFF),
            Hint: new Rgba(0xFF, 0x6B, 0x6B, 0xFF),
            Divider: new Rgba(0xE6, 0xE9, 0xEF, 0x40)
        );
}
