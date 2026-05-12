using System.Collections.Immutable;
using Linerule.Core;
using Linerule.Platform.Windows.Rendering;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Pure factory: <see cref="State"/> + <see cref="HotkeyMap"/> +
/// <see cref="RenderStatsSnapshot"/> → <see cref="HudContent"/>.
/// Lives apart from the rendering layer so the HUD's content shape is
/// testable without spinning up Win2D.
/// </summary>
public static class HudComposer
{
    public static HudContent Compose(
        State state,
        HotkeyMap hotkeys,
        RenderStatsSnapshot stats,
        RenderTiming timing,
        string? hint = null
    )
    {
        System.ArgumentNullException.ThrowIfNull(state);
        System.ArgumentNullException.ThrowIfNull(hotkeys);
        return new HudContent(
            Status: BuildStatus(state),
            Hotkeys: BuildHotkeys(hotkeys),
            Telemetry: BuildTelemetry(stats, timing),
            Hint: hint
        );
    }

    private static HudStatus BuildStatus(State state) =>
        new(
            Mode: state.Mode.ToString(),
            Visible: state.Visible,
            OpacityPercent: ToPercent(state.Config.Opacity.Value, max: 255),
            ThicknessPx: state.Config.Thickness.Value
        );

    private static ImmutableArray<HudHotkeyRow> BuildHotkeys(HotkeyMap map) =>
        [
            new HudHotkeyRow(map.CycleMode, "Cycle mode (hold to browse)"),
            new HudHotkeyRow(map.ToggleVisible, "Hold to suppress"),
            new HudHotkeyRow(map.Thicker, "Thicker (hold)"),
            new HudHotkeyRow(map.Thinner, "Thinner (hold)"),
            new HudHotkeyRow(map.MoreOpaque, "More opaque (hold)"),
            new HudHotkeyRow(map.LessOpaque, "Less opaque (hold)"),
            new HudHotkeyRow(map.Quit, "Quit"),
        ];

    private static HudTelemetry BuildTelemetry(RenderStatsSnapshot stats, RenderTiming timing) =>
        new(
            DisplayHz: timing.DisplayRefreshHz,
            TickP99Ms: stats.P99.TotalMilliseconds,
            FramesDropped: stats.DroppedFrames,
            CommitTimeouts: stats.CommitTimeouts
        );

    private static int ToPercent(int value, int max) =>
        (int)System.Math.Round(value * 100.0 / max, System.MidpointRounding.AwayFromZero);
}
