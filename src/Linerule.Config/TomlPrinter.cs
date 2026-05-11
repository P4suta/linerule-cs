using System.Globalization;
using System.Text;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Hand-rolled TOML emitter for the <see cref="UserConfig"/> schema. Stays AOT-friendly
/// (no reflection); also serves as the human-readable "what does this config look like
/// in full" output for <c>linerule config show</c> and <c>linerule config print-default</c>.
/// </summary>
public static class TomlPrinter
{
    public static string Render(UserConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var sb = new StringBuilder();

        WriteOverlay(sb, config.Overlay);
        sb.AppendLine();
        WriteHotkeys(sb, config.Hotkeys);
        sb.AppendLine();
        WriteInput(sb, config.Input);
        sb.AppendLine();
        WriteHud(sb, config.Hud);
        sb.AppendLine();
        WriteRender(sb, config.Render);

        return sb.ToString();
    }

    private static void WriteOverlay(StringBuilder sb, OverlayConfig overlay)
    {
        sb.AppendLine("[overlay]");
        AppendRgba(sb, "mask_color", overlay.MaskColor);
        AppendInt(sb, "thickness", overlay.Thickness.Value);
        AppendInt(sb, "opacity", overlay.Opacity.Value);
    }

    private static void WriteHotkeys(StringBuilder sb, HotkeyMap hk)
    {
        sb.AppendLine("[hotkeys]");
        AppendString(sb, "cycle_mode", hk.CycleMode);
        AppendString(sb, "toggle_visible", hk.ToggleVisible);
        AppendString(sb, "thicker", hk.Thicker);
        AppendString(sb, "thinner", hk.Thinner);
        AppendString(sb, "more_opaque", hk.MoreOpaque);
        AppendString(sb, "less_opaque", hk.LessOpaque);
        AppendString(sb, "quit", hk.Quit);
    }

    private static void WriteInput(StringBuilder sb, InputConfig input)
    {
        sb.AppendLine("[input.tap_step]");
        AppendInt(sb, "thickness", input.TapStep.Thickness);
        AppendInt(sb, "opacity", input.TapStep.Opacity);
        sb.AppendLine();
        sb.AppendLine("[input.repeat]");
        AppendInt(sb, "initial_delay_ms", input.Repeat.InitialDelayMs);
        AppendInt(sb, "long_press_threshold_ms", input.Repeat.LongPressThresholdMs);
        AppendInt(sb, "slow_repeat_interval_ms", input.Repeat.SlowRepeatIntervalMs);
        AppendInt(sb, "release_poll_ms", input.Repeat.ReleasePollMs);
    }

    private static void WriteHud(StringBuilder sb, HudConfig hud)
    {
        sb.AppendLine("[hud]");
        AppendFloat(sb, "base_opacity", hud.BaseOpacity);
        AppendFloat(sb, "fade_decay_px", hud.FadeDecayPx);
        AppendInt(sb, "telemetry_refresh_ms", checked((int)hud.TelemetryRefreshMs));
        AppendFloat(sb, "width_logical", hud.Geometry.WidthLogical);
        AppendFloat(sb, "height_logical", hud.Geometry.HeightLogical);
        AppendFloat(sb, "margin_logical", hud.Geometry.MarginLogical);
        sb.AppendLine();
        sb.AppendLine("[hud.padding]");
        AppendFloat(sb, "edge", hud.Padding.Edge);
        AppendFloat(sb, "section", hud.Padding.Section);
        AppendFloat(sb, "row", hud.Padding.Row);
        sb.AppendLine();
        sb.AppendLine("[hud.fonts]");
        AppendFloat(sb, "title", hud.Fonts.Title);
        AppendFloat(sb, "status", hud.Fonts.Status);
        AppendFloat(sb, "body", hud.Fonts.Body);
        AppendFloat(sb, "telemetry", hud.Fonts.Telemetry);
        AppendString(sb, "title_family", hud.Fonts.TitleFamily);
        AppendString(sb, "mono_family", hud.Fonts.MonoFamily);
        sb.AppendLine();
        sb.AppendLine("[hud.colors]");
        AppendRgba(sb, "background", hud.Colors.Background);
        AppendRgba(sb, "foreground", hud.Colors.Foreground);
        AppendRgba(sb, "subtle", hud.Colors.Subtle);
        AppendRgba(sb, "accent", hud.Colors.Accent);
        AppendRgba(sb, "hint", hud.Colors.Hint);
        AppendRgba(sb, "divider", hud.Colors.Divider);
    }

    private static void WriteRender(StringBuilder sb, RenderConfig render)
    {
        sb.AppendLine("[render]");
        AppendDouble(sb, "warn_ratio", render.WarnRatio);
        AppendInt(sb, "fallback_refresh_hz", render.FallbackRefreshHz);
    }

    private static void AppendRgba(StringBuilder sb, string key, Rgba c) =>
        sb.AppendLine($"{key} = {{ r = {c.R}, g = {c.G}, b = {c.B}, a = {c.A} }}");

    private static void AppendInt(StringBuilder sb, string key, int value) =>
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{key} = {value}"));

    private static void AppendFloat(StringBuilder sb, string key, float value) =>
        sb.AppendLine($"{key} = {FormatFloat(value)}");

    private static void AppendDouble(StringBuilder sb, string key, double value) =>
        sb.AppendLine($"{key} = {FormatDouble(value)}");

    private static void AppendString(StringBuilder sb, string key, string value) =>
        sb.AppendLine($"{key} = \"{value}\"");

    /// <summary>
    /// Floats render with at least one decimal place so the TOML parser sees a Float,
    /// not an Integer, on round-trip. <c>120</c> would parse back as <see cref="long"/>;
    /// <c>120.0</c> parses back as <see cref="double"/>.
    /// </summary>
    private static string FormatFloat(float v)
    {
        var s = v.ToString("0.0##############", CultureInfo.InvariantCulture);
        return s.Contains('.', StringComparison.Ordinal) ? s : s + ".0";
    }

    private static string FormatDouble(double v)
    {
        var s = v.ToString("0.0##############", CultureInfo.InvariantCulture);
        return s.Contains('.', StringComparison.Ordinal) ? s : s + ".0";
    }
}
