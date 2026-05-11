using Linerule.Core;
using Tomlyn.Model;

namespace Linerule.Config;

/// <summary>
/// Stage 3 of the config pipeline: walks the Tomlyn DOM and projects it into the
/// untrusted <see cref="RawUserConfig"/> shape. **No** validation here — every
/// type mismatch or missing value yields <see langword="null"/>, every unknown key gets
/// collected for the validator to report. The deserializer's contract is "shape
/// transcription only"; downstream stages own correctness.
/// </summary>
internal static class RawConfigDeserializer
{
    private static readonly IReadOnlySet<string> TopLevelKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "overlay",
        "hotkeys",
        "input",
        "hud",
        "render",
    };

    private static readonly IReadOnlySet<string> OverlayKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "mask_color",
        "thickness",
        "opacity",
    };

    private static readonly IReadOnlySet<string> HotkeysKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "cycle_mode",
        "toggle_visible",
        "thicker",
        "thinner",
        "more_opaque",
        "less_opaque",
        "quit",
    };

    private static readonly IReadOnlySet<string> InputKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "tap_step",
        "repeat",
    };

    private static readonly IReadOnlySet<string> TapStepKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "thickness",
        "opacity",
    };

    private static readonly IReadOnlySet<string> RepeatKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "initial_delay_ms",
        "long_press_threshold_ms",
        "slow_repeat_interval_ms",
        "release_poll_ms",
    };

    private static readonly IReadOnlySet<string> HudKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "base_opacity",
        "fade_decay_px",
        "telemetry_refresh_ms",
        "width_logical",
        "height_logical",
        "margin_logical",
        "padding",
        "fonts",
        "colors",
    };

    private static readonly IReadOnlySet<string> HudPaddingKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "edge",
        "section",
        "row",
    };

    private static readonly IReadOnlySet<string> HudFontsKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "title",
        "status",
        "body",
        "telemetry",
        "title_family",
        "mono_family",
    };

    private static readonly IReadOnlySet<string> HudColorsKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "background",
        "foreground",
        "subtle",
        "accent",
        "hint",
        "divider",
    };

    private static readonly IReadOnlySet<string> RenderKnown = new HashSet<string>(StringComparer.Ordinal)
    {
        "warn_ratio",
        "fallback_refresh_hz",
    };

    /// <summary>Project the Tomlyn DOM into a <see cref="RawUserConfig"/>.</summary>
    public static RawUserConfig Deserialize(TomlTable root, string? source)
    {
        _ = source;
        var topUnknown = UnknownKeys(root, TopLevelKnown);

        var overlay = TryGetTable(root, "overlay", out var overlayTable) ? ReadOverlay(overlayTable) : EmptyOverlay();
        var hotkeys = TryGetTable(root, "hotkeys", out var hotkeysTable) ? ReadHotkeys(hotkeysTable) : EmptyHotkeys();
        var input = TryGetTable(root, "input", out var inputTable) ? ReadInput(inputTable) : EmptyInput();
        var hud = TryGetTable(root, "hud", out var hudTable) ? ReadHud(hudTable) : EmptyHud();
        var render = TryGetTable(root, "render", out var renderTable) ? ReadRender(renderTable) : EmptyRender();

        return new RawUserConfig(overlay, hotkeys, input, hud, render, topUnknown);
    }

    private static RawOverlayConfig ReadOverlay(TomlTable t) =>
        new(
            MaskColor: TryGetRgba(t, "mask_color"),
            Thickness: TryGetInt(t, "thickness"),
            Opacity: TryGetInt(t, "opacity"),
            UnknownKeys: UnknownKeys(t, OverlayKnown)
        );

    private static RawHotkeyMap ReadHotkeys(TomlTable t) =>
        new(
            CycleMode: TryGetString(t, "cycle_mode"),
            ToggleVisible: TryGetString(t, "toggle_visible"),
            Thicker: TryGetString(t, "thicker"),
            Thinner: TryGetString(t, "thinner"),
            MoreOpaque: TryGetString(t, "more_opaque"),
            LessOpaque: TryGetString(t, "less_opaque"),
            Quit: TryGetString(t, "quit"),
            UnknownKeys: UnknownKeys(t, HotkeysKnown)
        );

    private static RawInputConfig ReadInput(TomlTable t)
    {
        var tap = TryGetTable(t, "tap_step", out var tapTable) ? ReadTapStep(tapTable) : null;
        var rep = TryGetTable(t, "repeat", out var repTable) ? ReadRepeat(repTable) : null;
        return new RawInputConfig(tap, rep, UnknownKeys(t, InputKnown));
    }

    private static RawTapStepConfig ReadTapStep(TomlTable t) =>
        new(
            Thickness: TryGetInt(t, "thickness"),
            Opacity: TryGetInt(t, "opacity"),
            UnknownKeys: UnknownKeys(t, TapStepKnown)
        );

    private static RawRepeatConfig ReadRepeat(TomlTable t) =>
        new(
            InitialDelayMs: TryGetInt(t, "initial_delay_ms"),
            LongPressThresholdMs: TryGetInt(t, "long_press_threshold_ms"),
            SlowRepeatIntervalMs: TryGetInt(t, "slow_repeat_interval_ms"),
            ReleasePollMs: TryGetInt(t, "release_poll_ms"),
            UnknownKeys: UnknownKeys(t, RepeatKnown)
        );

    private static RawHudConfig ReadHud(TomlTable t)
    {
        var padding = TryGetTable(t, "padding", out var padT) ? ReadHudPadding(padT) : null;
        var fonts = TryGetTable(t, "fonts", out var fontT) ? ReadHudFonts(fontT) : null;
        var colors = TryGetTable(t, "colors", out var colT) ? ReadHudColors(colT) : null;
        return new RawHudConfig(
            BaseOpacity: TryGetFloat(t, "base_opacity"),
            FadeDecayPx: TryGetFloat(t, "fade_decay_px"),
            TelemetryRefreshMs: TryGetLong(t, "telemetry_refresh_ms"),
            WidthLogical: TryGetFloat(t, "width_logical"),
            HeightLogical: TryGetFloat(t, "height_logical"),
            MarginLogical: TryGetFloat(t, "margin_logical"),
            Padding: padding,
            Fonts: fonts,
            Colors: colors,
            UnknownKeys: UnknownKeys(t, HudKnown)
        );
    }

    private static RawHudPadding ReadHudPadding(TomlTable t) =>
        new(
            Edge: TryGetFloat(t, "edge"),
            Section: TryGetFloat(t, "section"),
            Row: TryGetFloat(t, "row"),
            UnknownKeys: UnknownKeys(t, HudPaddingKnown)
        );

    private static RawHudFonts ReadHudFonts(TomlTable t) =>
        new(
            Title: TryGetFloat(t, "title"),
            Status: TryGetFloat(t, "status"),
            Body: TryGetFloat(t, "body"),
            Telemetry: TryGetFloat(t, "telemetry"),
            TitleFamily: TryGetString(t, "title_family"),
            MonoFamily: TryGetString(t, "mono_family"),
            UnknownKeys: UnknownKeys(t, HudFontsKnown)
        );

    private static RawHudColors ReadHudColors(TomlTable t) =>
        new(
            Background: TryGetRgba(t, "background"),
            Foreground: TryGetRgba(t, "foreground"),
            Subtle: TryGetRgba(t, "subtle"),
            Accent: TryGetRgba(t, "accent"),
            Hint: TryGetRgba(t, "hint"),
            Divider: TryGetRgba(t, "divider"),
            UnknownKeys: UnknownKeys(t, HudColorsKnown)
        );

    private static RawRenderConfig ReadRender(TomlTable t) =>
        new(
            WarnRatio: TryGetDouble(t, "warn_ratio"),
            FallbackRefreshHz: TryGetInt(t, "fallback_refresh_hz"),
            UnknownKeys: UnknownKeys(t, RenderKnown)
        );

    // --- Empty (when section is missing) ---

    private static readonly IReadOnlyList<string> NoUnknown = [];

    private static RawOverlayConfig EmptyOverlay() =>
        new(MaskColor: null, Thickness: null, Opacity: null, UnknownKeys: NoUnknown);

    private static RawHotkeyMap EmptyHotkeys() =>
        new(
            CycleMode: null,
            ToggleVisible: null,
            Thicker: null,
            Thinner: null,
            MoreOpaque: null,
            LessOpaque: null,
            Quit: null,
            UnknownKeys: NoUnknown
        );

    private static RawInputConfig EmptyInput() => new(TapStep: null, Repeat: null, UnknownKeys: NoUnknown);

    private static RawHudConfig EmptyHud() =>
        new(
            BaseOpacity: null,
            FadeDecayPx: null,
            TelemetryRefreshMs: null,
            WidthLogical: null,
            HeightLogical: null,
            MarginLogical: null,
            Padding: null,
            Fonts: null,
            Colors: null,
            UnknownKeys: NoUnknown
        );

    private static RawRenderConfig EmptyRender() =>
        new(WarnRatio: null, FallbackRefreshHz: null, UnknownKeys: NoUnknown);

    // --- Tomlyn DOM accessors ---

    private static bool TryGetTable(TomlTable parent, string key, out TomlTable child)
    {
        if (parent.TryGetValue(key, out var raw) && raw is TomlTable t)
        {
            child = t;
            return true;
        }
        child = null!;
        return false;
    }

    private static int? TryGetInt(TomlTable t, string key)
    {
        return !t.TryGetValue(key, out var v)
            ? null
            : v switch
            {
                long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
                _ => null,
            };
    }

    private static long? TryGetLong(TomlTable t, string key) => t.TryGetValue(key, out var v) && v is long l ? l : null;

    private static float? TryGetFloat(TomlTable t, string key)
    {
        var d = TryGetDouble(t, key);
        return d is null ? null : (float)d.Value;
    }

    private static double? TryGetDouble(TomlTable t, string key)
    {
        return !t.TryGetValue(key, out var v)
            ? null
            : v switch
            {
                double d => d,
                long l => l,
                _ => null,
            };
    }

    private static string? TryGetString(TomlTable t, string key)
    {
        return !t.TryGetValue(key, out var v) ? null : v as string;
    }

    private static Rgba? TryGetRgba(TomlTable parent, string key)
    {
        if (!parent.TryGetValue(key, out var raw))
        {
            return null;
        }
        if (raw is not TomlTable t)
        {
            return null;
        }
        // Read all four; missing component ⇒ null component ⇒ whole color null
        // (Validator will surface the typed reason).
        var r = TryGetInt(t, "r");
        var g = TryGetInt(t, "g");
        var b = TryGetInt(t, "b");
        var a = TryGetInt(t, "a");
        return
            r is null
            || g is null
            || b is null
            || a is null
            || r is < 0 or > 255
            || g is < 0 or > 255
            || b is < 0 or > 255
            || a is < 0 or > 255
            ? null
            : new Rgba((byte)r.Value, (byte)g.Value, (byte)b.Value, (byte)a.Value);
    }

    private static IReadOnlyList<string> UnknownKeys(TomlTable table, IReadOnlySet<string> known)
    {
        var unknown = table.Keys.Where(key => !known.Contains(key)).ToList();
        return unknown.Count == 0 ? NoUnknown : unknown;
    }
}
