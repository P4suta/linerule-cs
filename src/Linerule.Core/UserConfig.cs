namespace Linerule.Core;

/// <summary>
/// Top-level user tunables: a 5-way product of typed records, one per concern.
/// Per ADR-0015 (Tunables as compile-time constants), <see cref="Default"/> is
/// the single source of truth — there is no runtime config file to load. The
/// record stays a typed product so consumers continue to take dependency-
/// injected <see cref="UserConfig"/> values rather than scattered statics.
/// (Named <c>UserConfig</c> rather than the plain <c>Config</c> to avoid the
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
