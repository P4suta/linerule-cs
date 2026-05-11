using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Top-level user config: a 5-way product of typed records, one per concern.
/// Round-trips with the Rust-side <c>config.toml</c> schema. Construct via
/// <see cref="ConfigLoader"/> — never directly from raw input. (Named
/// <c>UserConfig</c> rather than the plain <c>Config</c> to avoid the
/// type-name-matches-namespace pitfall flagged by MA0049 / CA1724.)
/// </summary>
public sealed record UserConfig(
    OverlayConfig Overlay,
    HotkeyMap Hotkeys,
    InputConfig Input,
    HudConfig Hud,
    RenderConfig Render
)
{
    public static UserConfig Default { get; } =
        new(
            Overlay: OverlayConfig.Default,
            Hotkeys: HotkeyMap.Default,
            Input: InputConfig.Default,
            Hud: HudConfig.Default,
            Render: RenderConfig.Default
        );
}
