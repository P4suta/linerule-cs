namespace Linerule.Config;

internal sealed record RawHotkeyMap(
    string? CycleMode,
    string? ToggleVisible,
    string? Thicker,
    string? Thinner,
    string? MoreOpaque,
    string? LessOpaque,
    string? Quit,
    IReadOnlyList<string> UnknownKeys
);
