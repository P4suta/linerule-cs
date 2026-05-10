using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Top-level user config. Round-trips with the Rust-side <c>config.toml</c> schema.
/// Construct via <see cref="ConfigLoader"/> — never directly from raw input.
/// (Named <c>UserConfig</c> rather than the plain <c>Config</c> to avoid the
/// type-name-matches-namespace pitfall flagged by MA0049 / CA1724.)
/// </summary>
public sealed record UserConfig(OverlayConfig Overlay, HotkeyMap Hotkeys)
{
    public static UserConfig Default { get; } = new(OverlayConfig.Default, HotkeyMap.Default);
}
