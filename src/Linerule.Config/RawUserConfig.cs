namespace Linerule.Config;

/// <summary>
/// Untrusted, nullable-field intermediate DTO produced by
/// <see cref="RawConfigDeserializer"/>. Each section carries the set of unknown
/// keys it saw (for typo-detection diagnostics in <see cref="Validator"/>).
/// Never used outside the loader pipeline.
/// </summary>
internal sealed record RawUserConfig(
    RawOverlayConfig Overlay,
    RawHotkeyMap Hotkeys,
    RawInputConfig Input,
    RawHudConfig Hud,
    RawRenderConfig Render,
    IReadOnlyList<string> UnknownTopLevelKeys
);
