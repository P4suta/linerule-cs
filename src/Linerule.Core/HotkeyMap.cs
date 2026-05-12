namespace Linerule.Core;

/// <summary>
/// Mapping from action to chord string (e.g. <c>"Ctrl+Alt+R"</c>). The strings
/// are validated by the <c>Linerule.Platform</c> chord parser at hotkey-registration
/// time, not here — this layer is purely the TOML schema and its defaults.
/// </summary>
public sealed record HotkeyMap(
    string CycleMode,
    string ToggleVisible,
    string Thicker,
    string Thinner,
    string MoreOpaque,
    string LessOpaque,
    string Quit
)
{
    public static HotkeyMap Default { get; } =
        new(
            CycleMode: "Ctrl+Alt+R",
            ToggleVisible: "Ctrl+Alt+H",
            Thicker: "Ctrl+Alt+]",
            Thinner: "Ctrl+Alt+[",
            MoreOpaque: "Ctrl+Alt+=",
            LessOpaque: "Ctrl+Alt+-",
            Quit: "Ctrl+Alt+Q"
        );
}
