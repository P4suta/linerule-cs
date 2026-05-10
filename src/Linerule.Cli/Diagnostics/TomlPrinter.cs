using System.Text;
using Linerule.Config;
using Linerule.Core;

namespace Linerule.Cli.Diagnostics;

/// <summary>
/// Hand-rolled TOML emitter for the small <see cref="UserConfig"/> schema.
/// Avoids reflection so this stays AOT-friendly even before Tomlyn's writer is verified.
/// </summary>
internal static class TomlPrinter
{
    public static string Render(UserConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var sb = new StringBuilder();

        sb.AppendLine("[overlay]");
        AppendRgba(sb, "mask_color", config.Overlay.MaskColor);
        sb.AppendLine($"thickness = {config.Overlay.Thickness.Value}");
        sb.AppendLine($"opacity = {config.Overlay.Opacity.Value}");
        sb.AppendLine();

        sb.AppendLine("[hotkeys]");
        sb.AppendLine($"cycle_mode = \"{config.Hotkeys.CycleMode}\"");
        sb.AppendLine($"toggle_visible = \"{config.Hotkeys.ToggleVisible}\"");
        sb.AppendLine($"thicker = \"{config.Hotkeys.Thicker}\"");
        sb.AppendLine($"thinner = \"{config.Hotkeys.Thinner}\"");
        sb.AppendLine($"more_opaque = \"{config.Hotkeys.MoreOpaque}\"");
        sb.AppendLine($"less_opaque = \"{config.Hotkeys.LessOpaque}\"");
        sb.AppendLine($"quit = \"{config.Hotkeys.Quit}\"");

        return sb.ToString();
    }

    private static void AppendRgba(StringBuilder sb, string key, Rgba c) =>
        sb.AppendLine($"{key} = {{ r = {c.R}, g = {c.G}, b = {c.B}, a = {c.A} }}");
}
