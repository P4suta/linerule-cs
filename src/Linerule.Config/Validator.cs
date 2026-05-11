using System.Globalization;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Stage 4 of the config pipeline: turn the untrusted <see cref="RawUserConfig"/>
/// into a fully-validated <see cref="UserConfig"/>, or aggregate the diagnostics
/// explaining why it cannot. Responsibilities:
/// <list type="bullet">
///   <item>Per-field range checks (e.g. <c>hud.base_opacity ∈ [0, 1]</c>).</item>
///   <item>Smart-constructor validation via <c>Linerule.Core</c> newtypes
///         (<see cref="Thickness.TryCreate"/>, <see cref="Opacity.TryCreate"/>).</item>
///   <item>Cross-field invariants (HUD geometry vs padding, repeat timings, color
///         identity / contrast, hotkey chord uniqueness, render budget warn ratio).</item>
///   <item>Unknown-key reporting at <see cref="DiagnosticSeverity.Warning"/>.</item>
/// </list>
/// </summary>
internal static class Validator
{
    public static Result<UserConfig, ConfigError> Validate(
        RawUserConfig raw,
        string? source,
        IReadOnlyList<ConfigDiagnostic>? carry = null
    )
    {
        var diags = SeedDiagnostics(carry);
        ReportAllUnknownKeys(raw, source, diags);
        var (overlay, hotkeys, input, hud, render) = BuildValidatedSections(raw, source, diags);
        ValidateCrossField(overlay, hotkeys, input, hud, render, source, diags);

        return diags.Exists(d => d.IsError)
            ? Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([.. diags]))
            : Result.Ok<UserConfig, ConfigError>(new UserConfig(overlay, hotkeys, input, hud, render));
    }

    private static List<ConfigDiagnostic> SeedDiagnostics(IReadOnlyList<ConfigDiagnostic>? carry)
    {
        var diags = new List<ConfigDiagnostic>(carry?.Count ?? 0);
        if (carry is { Count: > 0 })
        {
            diags.AddRange(carry);
        }
        return diags;
    }

    private static void ReportAllUnknownKeys(RawUserConfig raw, string? source, List<ConfigDiagnostic> diags)
    {
        ReportUnknownKeys(raw.UnknownTopLevelKeys, dotPathPrefix: string.Empty, source, diags, hint: KnownTopLevel);
        ReportUnknownKeys(raw.Overlay.UnknownKeys, "overlay", source, diags, hint: null);
        ReportUnknownKeys(raw.Hotkeys.UnknownKeys, "hotkeys", source, diags, hint: null);
        ReportUnknownKeys(raw.Input.UnknownKeys, "input", source, diags, hint: null);
        if (raw.Input.TapStep is { } ts)
        {
            ReportUnknownKeys(ts.UnknownKeys, "input.tap_step", source, diags, hint: null);
        }
        if (raw.Input.Repeat is { } rp)
        {
            ReportUnknownKeys(rp.UnknownKeys, "input.repeat", source, diags, hint: null);
        }
        ReportUnknownKeys(raw.Hud.UnknownKeys, "hud", source, diags, hint: null);
        if (raw.Hud.Padding is { } pd)
        {
            ReportUnknownKeys(pd.UnknownKeys, "hud.padding", source, diags, hint: null);
        }
        if (raw.Hud.Fonts is { } fn)
        {
            ReportUnknownKeys(fn.UnknownKeys, "hud.fonts", source, diags, hint: null);
        }
        if (raw.Hud.Colors is { } cl)
        {
            ReportUnknownKeys(cl.UnknownKeys, "hud.colors", source, diags, hint: null);
        }
        ReportUnknownKeys(raw.Render.UnknownKeys, "render", source, diags, hint: null);
    }

    private static (
        OverlayConfig Overlay,
        HotkeyMap Hotkeys,
        InputConfig Input,
        HudConfig Hud,
        RenderConfig Render
    ) BuildValidatedSections(RawUserConfig raw, string? source, List<ConfigDiagnostic> diags) =>
        (
            ValidateOverlay(raw.Overlay, source, diags),
            ValidateHotkeys(raw.Hotkeys, source, diags),
            ValidateInput(raw.Input, source, diags),
            ValidateHud(raw.Hud, source, diags),
            ValidateRender(raw.Render, source, diags)
        );

    private static readonly string[] KnownTopLevel = ["overlay", "hotkeys", "input", "hud", "render"];

    private static void ReportUnknownKeys(
        IReadOnlyList<string> unknown,
        string dotPathPrefix,
        string? source,
        List<ConfigDiagnostic> diags,
        string[]? hint
    )
    {
        if (unknown.Count == 0)
        {
            return;
        }
        foreach (var key in unknown)
        {
            var full = string.IsNullOrEmpty(dotPathPrefix) ? key : $"{dotPathPrefix}.{key}";
            diags.Add(
                new ConfigDiagnostic(
                    Message: $"unknown key `{full}` — ignored",
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: full,
                    Suggestion: hint is { Length: > 0 }
                        ? $"Did you mean one of: {string.Join(", ", hint)}?"
                        : "Check for typos against the documented schema."
                )
            );
        }
    }

    // ----- Per-section validation -----

    private static OverlayConfig ValidateOverlay(RawOverlayConfig raw, string? source, List<ConfigDiagnostic> diags)
    {
        var def = OverlayConfig.Default;
        var mask = raw.MaskColor ?? def.MaskColor;
        var thickness = raw.Thickness switch
        {
            null => def.Thickness,
            int v => Thickness.TryCreate(v) switch
            {
                Result<Thickness, CoreError>.Ok ok => ok.Value,
                Result<Thickness, CoreError>.Err err => RejectAndDefault(
                    diags,
                    source,
                    "overlay.thickness",
                    err.Error,
                    def.Thickness
                ),
                _ => def.Thickness,
            },
        };
        var opacity = raw.Opacity switch
        {
            null => def.Opacity,
            int v => Opacity.TryCreate(v) switch
            {
                Result<Opacity, CoreError>.Ok ok => ok.Value,
                Result<Opacity, CoreError>.Err err => RejectAndDefault(
                    diags,
                    source,
                    "overlay.opacity",
                    err.Error,
                    def.Opacity
                ),
                _ => def.Opacity,
            },
        };
        return new OverlayConfig(mask, thickness, opacity);
    }

    private static T RejectAndDefault<T>(
        List<ConfigDiagnostic> diags,
        string? source,
        string dotPath,
        CoreError error,
        T fallback
    )
    {
        diags.Add(
            new ConfigDiagnostic(
                Message: error.ToHumanString(),
                Source: source,
                Span: null,
                Severity: DiagnosticSeverity.Error,
                DotPath: dotPath
            )
        );
        return fallback;
    }

    private static HotkeyMap ValidateHotkeys(RawHotkeyMap raw, string? source, List<ConfigDiagnostic> diags)
    {
        _ = source;
        _ = diags;
        var def = HotkeyMap.Default;
        return new HotkeyMap(
            CycleMode: raw.CycleMode ?? def.CycleMode,
            ToggleVisible: raw.ToggleVisible ?? def.ToggleVisible,
            Thicker: raw.Thicker ?? def.Thicker,
            Thinner: raw.Thinner ?? def.Thinner,
            MoreOpaque: raw.MoreOpaque ?? def.MoreOpaque,
            LessOpaque: raw.LessOpaque ?? def.LessOpaque,
            Quit: raw.Quit ?? def.Quit
        );
    }

    private static InputConfig ValidateInput(RawInputConfig raw, string? source, List<ConfigDiagnostic> diags)
    {
        var defTap = TapStepConfig.Default;
        var tap = raw.TapStep is { } ts
            ? new TapStepConfig(
                Thickness: CheckIntRange(
                    ts.Thickness,
                    defTap.Thickness,
                    "input.tap_step.thickness",
                    1,
                    256,
                    source,
                    diags
                ),
                Opacity: CheckIntRange(ts.Opacity, defTap.Opacity, "input.tap_step.opacity", 1, 256, source, diags)
            )
            : defTap;

        var defRep = RepeatConfig.Default;
        var rep = raw.Repeat is { } rp
            ? new RepeatConfig(
                InitialDelayMs: CheckIntRange(
                    rp.InitialDelayMs,
                    defRep.InitialDelayMs,
                    "input.repeat.initial_delay_ms",
                    50,
                    2000,
                    source,
                    diags
                ),
                LongPressThresholdMs: CheckIntRange(
                    rp.LongPressThresholdMs,
                    defRep.LongPressThresholdMs,
                    "input.repeat.long_press_threshold_ms",
                    50,
                    2000,
                    source,
                    diags
                ),
                SlowRepeatIntervalMs: CheckIntRange(
                    rp.SlowRepeatIntervalMs,
                    defRep.SlowRepeatIntervalMs,
                    "input.repeat.slow_repeat_interval_ms",
                    50,
                    2000,
                    source,
                    diags
                ),
                ReleasePollMs: CheckIntRange(
                    rp.ReleasePollMs,
                    defRep.ReleasePollMs,
                    "input.repeat.release_poll_ms",
                    16,
                    200,
                    source,
                    diags
                )
            )
            : defRep;

        return new InputConfig(tap, rep);
    }

    private static HudConfig ValidateHud(RawHudConfig raw, string? source, List<ConfigDiagnostic> diags)
    {
        var def = HudConfig.Default;
        var baseOpacity = CheckFloatRange(raw.BaseOpacity, def.BaseOpacity, "hud.base_opacity", 0f, 1f, source, diags);
        var fadeDecay = CheckFloatRange(
            raw.FadeDecayPx,
            def.FadeDecayPx,
            "hud.fade_decay_px",
            1f,
            1000f,
            source,
            diags
        );
        var telemetryMs = CheckLongRange(
            raw.TelemetryRefreshMs,
            def.TelemetryRefreshMs,
            "hud.telemetry_refresh_ms",
            50L,
            2000L,
            source,
            diags
        );

        var geometry = ValidateHudGeometry(raw, def.Geometry, source, diags);
        var padding = ValidateHudPadding(raw.Padding, def.Padding, source, diags);
        var fonts = ValidateHudFonts(raw.Fonts, def.Fonts, source, diags);
        var colors = ValidateHudColors(raw.Colors, def.Colors);

        return new HudConfig(baseOpacity, fadeDecay, telemetryMs, geometry, padding, fonts, colors);
    }

    private static HudGeometry ValidateHudGeometry(
        RawHudConfig raw,
        HudGeometry def,
        string? source,
        List<ConfigDiagnostic> diags
    ) =>
        new(
            WidthLogical: CheckFloatRange(
                raw.WidthLogical,
                def.WidthLogical,
                "hud.width_logical",
                1f,
                10000f,
                source,
                diags
            ),
            HeightLogical: CheckFloatRange(
                raw.HeightLogical,
                def.HeightLogical,
                "hud.height_logical",
                1f,
                10000f,
                source,
                diags
            ),
            MarginLogical: CheckFloatRange(
                raw.MarginLogical,
                def.MarginLogical,
                "hud.margin_logical",
                0f,
                10000f,
                source,
                diags
            )
        );

    private static HudPadding ValidateHudPadding(
        RawHudPadding? raw,
        HudPadding def,
        string? source,
        List<ConfigDiagnostic> diags
    ) =>
        raw is { } pd
            ? new HudPadding(
                Edge: CheckFloatRange(pd.Edge, def.Edge, "hud.padding.edge", 0f, 1000f, source, diags),
                Section: CheckFloatRange(pd.Section, def.Section, "hud.padding.section", 0f, 1000f, source, diags),
                Row: CheckFloatRange(pd.Row, def.Row, "hud.padding.row", 0f, 1000f, source, diags)
            )
            : def;

    private static HudFonts ValidateHudFonts(
        RawHudFonts? raw,
        HudFonts def,
        string? source,
        List<ConfigDiagnostic> diags
    ) =>
        raw is { } fn
            ? new HudFonts(
                Title: CheckFloatRange(fn.Title, def.Title, "hud.fonts.title", 1f, 200f, source, diags),
                Status: CheckFloatRange(fn.Status, def.Status, "hud.fonts.status", 1f, 200f, source, diags),
                Body: CheckFloatRange(fn.Body, def.Body, "hud.fonts.body", 1f, 200f, source, diags),
                Telemetry: CheckFloatRange(fn.Telemetry, def.Telemetry, "hud.fonts.telemetry", 1f, 200f, source, diags),
                TitleFamily: fn.TitleFamily ?? def.TitleFamily,
                MonoFamily: fn.MonoFamily ?? def.MonoFamily
            )
            : def;

    private static HudColors ValidateHudColors(RawHudColors? raw, HudColors def) =>
        raw is { } cl
            ? new HudColors(
                Background: cl.Background ?? def.Background,
                Foreground: cl.Foreground ?? def.Foreground,
                Subtle: cl.Subtle ?? def.Subtle,
                Accent: cl.Accent ?? def.Accent,
                Hint: cl.Hint ?? def.Hint,
                Divider: cl.Divider ?? def.Divider
            )
            : def;

    private static RenderConfig ValidateRender(RawRenderConfig raw, string? source, List<ConfigDiagnostic> diags)
    {
        var def = RenderConfig.Default;
        var warn = CheckDoubleRange(raw.WarnRatio, def.WarnRatio, "render.warn_ratio", 0.1, 1.0, source, diags);
        var hz = CheckIntRange(
            raw.FallbackRefreshHz,
            def.FallbackRefreshHz,
            "render.fallback_refresh_hz",
            1,
            480,
            source,
            diags
        );
        return new RenderConfig(warn, hz);
    }

    // ----- Range checks (rejected values fall back to default + Error diagnostic) -----

    private static int CheckIntRange(
        int? v,
        int fallback,
        string dotPath,
        int min,
        int max,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (v is null)
        {
            return fallback;
        }
        if (v.Value < min || v.Value > max)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(CultureInfo.InvariantCulture, $"{v.Value} is out of range [{min}, {max}]"),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: dotPath,
                    Suggestion: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Set a value between {min} and {max}; the default is {fallback}."
                    )
                )
            );
            return fallback;
        }
        return v.Value;
    }

    private static long CheckLongRange(
        long? v,
        long fallback,
        string dotPath,
        long min,
        long max,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (v is null)
        {
            return fallback;
        }
        if (v.Value < min || v.Value > max)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(CultureInfo.InvariantCulture, $"{v.Value} is out of range [{min}, {max}]"),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: dotPath,
                    Suggestion: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Set a value between {min} and {max}; the default is {fallback}."
                    )
                )
            );
            return fallback;
        }
        return v.Value;
    }

    private static float CheckFloatRange(
        float? v,
        float fallback,
        string dotPath,
        float min,
        float max,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (v is null)
        {
            return fallback;
        }
        if (float.IsNaN(v.Value) || v.Value < min || v.Value > max)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(CultureInfo.InvariantCulture, $"{v.Value} is out of range [{min}, {max}]"),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: dotPath,
                    Suggestion: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Set a value between {min} and {max}; the default is {fallback}."
                    )
                )
            );
            return fallback;
        }
        return v.Value;
    }

    private static double CheckDoubleRange(
        double? v,
        double fallback,
        string dotPath,
        double min,
        double max,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (v is null)
        {
            return fallback;
        }
        if (double.IsNaN(v.Value) || v.Value < min || v.Value > max)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(CultureInfo.InvariantCulture, $"{v.Value} is out of range [{min}, {max}]"),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: dotPath,
                    Suggestion: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Set a value between {min} and {max}; the default is {fallback}."
                    )
                )
            );
            return fallback;
        }
        return v.Value;
    }

    // ----- Cross-field invariants (heavy set per plan §PR1) -----

    private static void ValidateCrossField(
        OverlayConfig overlay,
        HotkeyMap hotkeys,
        InputConfig input,
        HudConfig hud,
        RenderConfig render,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        _ = overlay;
        CheckHudPaddingFitsWidth(hud, source, diags);
        CheckHudPaddingFitsHeight(hud, source, diags);
        CheckRepeatInitialDelayVsThreshold(input, source, diags);
        CheckRepeatSlowVsRelease(input, source, diags);
        CheckForegroundBackgroundDistinct(hud, source, diags);
        CheckHotkeyChordsUnique(hotkeys, source, diags);
        CheckRenderWarnRatioSane(render, source, diags);
    }

    /// <summary>HUD width must accommodate edge padding on both sides.</summary>
    private static void CheckHudPaddingFitsWidth(HudConfig hud, string? source, List<ConfigDiagnostic> diags)
    {
        if (hud.Geometry.WidthLogical < 2f * hud.Padding.Edge)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"HUD width ({hud.Geometry.WidthLogical}) is too narrow for the requested edge padding ({hud.Padding.Edge} × 2 = {2f * hud.Padding.Edge})."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: "hud.padding.edge",
                    Suggestion: "Either increase `hud.width_logical` or decrease `hud.padding.edge`.",
                    Related: ["hud.width_logical"]
                )
            );
        }
    }

    /// <summary>HUD height must accommodate edge padding on both sides.</summary>
    private static void CheckHudPaddingFitsHeight(HudConfig hud, string? source, List<ConfigDiagnostic> diags)
    {
        if (hud.Geometry.HeightLogical < 2f * hud.Padding.Edge)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"HUD height ({hud.Geometry.HeightLogical}) is too narrow for the requested edge padding ({hud.Padding.Edge} × 2 = {2f * hud.Padding.Edge})."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: "hud.padding.edge",
                    Suggestion: "Either increase `hud.height_logical` or decrease `hud.padding.edge`.",
                    Related: ["hud.height_logical"]
                )
            );
        }
    }

    /// <summary>Initial delay should not be shorter than the long-press threshold.</summary>
    private static void CheckRepeatInitialDelayVsThreshold(
        InputConfig input,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (input.Repeat.InitialDelayMs < input.Repeat.LongPressThresholdMs)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Initial delay ({input.Repeat.InitialDelayMs} ms) is shorter than the long-press threshold ({input.Repeat.LongPressThresholdMs} ms); long-press detection will never fire because the repeat starts first."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: "input.repeat.initial_delay_ms",
                    Suggestion: "Set `initial_delay_ms` ≥ `long_press_threshold_ms`.",
                    Related: ["input.repeat.long_press_threshold_ms"]
                )
            );
        }
    }

    /// <summary>Slow-repeat interval should not be finer than release-poll cadence.</summary>
    private static void CheckRepeatSlowVsRelease(InputConfig input, string? source, List<ConfigDiagnostic> diags)
    {
        if (input.Repeat.SlowRepeatIntervalMs < input.Repeat.ReleasePollMs)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Slow-repeat interval ({input.Repeat.SlowRepeatIntervalMs} ms) is finer than release-poll cadence ({input.Repeat.ReleasePollMs} ms); release may be missed between intervals."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: "input.repeat.slow_repeat_interval_ms",
                    Suggestion: "Set `slow_repeat_interval_ms` ≥ `release_poll_ms`.",
                    Related: ["input.repeat.release_poll_ms"]
                )
            );
        }
    }

    /// <summary>
    /// Foreground vs background identical RGB ⇒ invisible text (Error).
    /// Otherwise WCAG simple contrast ratio &lt; 3.0 ⇒ Warning.
    /// </summary>
    private static void CheckForegroundBackgroundDistinct(HudConfig hud, string? source, List<ConfigDiagnostic> diags)
    {
        if (RgbEqual(hud.Colors.Foreground, hud.Colors.Background))
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: "Foreground and background colors are identical; HUD text will be invisible.",
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Error,
                    DotPath: "hud.colors.foreground",
                    Suggestion: "Set `hud.colors.foreground` to a contrasting color.",
                    Related: ["hud.colors.background"]
                )
            );
            return;
        }
        CheckContrastRatio(hud, source, diags);
    }

    /// <summary>WCAG simple contrast ratio &lt; 3.0 ⇒ Warning.</summary>
    private static void CheckContrastRatio(HudConfig hud, string? source, List<ConfigDiagnostic> diags)
    {
        var ratio = ContrastRatio(hud.Colors.Foreground, hud.Colors.Background);
        if (ratio < 3.0)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"Foreground / background contrast ratio is {ratio:F2}, below the WCAG-3 minimum of 3.0; HUD text may be hard to read."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: "hud.colors.foreground",
                    Suggestion: "Brighten the foreground or darken the background until the ratio is ≥ 3.0.",
                    Related: ["hud.colors.background"]
                )
            );
        }
    }

    /// <summary>Duplicate hotkey chord assignments.</summary>
    private static void CheckHotkeyChordsUnique(HotkeyMap hotkeys, string? source, List<ConfigDiagnostic> diags)
    {
        var chordMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        AddChord(chordMap, hotkeys.CycleMode, "hotkeys.cycle_mode");
        AddChord(chordMap, hotkeys.ToggleVisible, "hotkeys.toggle_visible");
        AddChord(chordMap, hotkeys.Thicker, "hotkeys.thicker");
        AddChord(chordMap, hotkeys.Thinner, "hotkeys.thinner");
        AddChord(chordMap, hotkeys.MoreOpaque, "hotkeys.more_opaque");
        AddChord(chordMap, hotkeys.LessOpaque, "hotkeys.less_opaque");
        AddChord(chordMap, hotkeys.Quit, "hotkeys.quit");
        foreach (var (chord, names) in chordMap)
        {
            if (names.Count > 1)
            {
                diags.Add(
                    new ConfigDiagnostic(
                        Message: $"Hotkey chord `{chord}` is bound to more than one action: {string.Join(", ", names)}. Only one will take effect.",
                        Source: source,
                        Span: null,
                        Severity: DiagnosticSeverity.Error,
                        DotPath: names[0],
                        Suggestion: "Reassign one of the conflicting hotkeys to a different chord.",
                        Related: [.. names.Skip(1)]
                    )
                );
            }
        }
    }

    /// <summary>Render warn ratio dangerously high.</summary>
    private static void CheckRenderWarnRatioSane(RenderConfig render, string? source, List<ConfigDiagnostic> diags)
    {
        if (render.WarnRatio > 0.95)
        {
            diags.Add(
                new ConfigDiagnostic(
                    Message: string.Create(
                        CultureInfo.InvariantCulture,
                        $"`render.warn_ratio` = {render.WarnRatio:F2} leaves less than 1 ms of vsync headroom on a typical 60 Hz display; expect spurious budget-overrun warnings."
                    ),
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: "render.warn_ratio",
                    Suggestion: "0.8 is the documented default and balances early warning with quiet steady-state."
                )
            );
        }
    }

    private static void AddChord(Dictionary<string, List<string>> map, string chord, string dotPath)
    {
        if (!map.TryGetValue(chord, out var list))
        {
            list = [];
            map[chord] = list;
        }
        list.Add(dotPath);
    }

    private static bool RgbEqual(Rgba a, Rgba b) => a.R == b.R && a.G == b.G && a.B == b.B;

    /// <summary>WCAG-style simple contrast ratio using relative luminance.</summary>
    private static double ContrastRatio(Rgba fg, Rgba bg)
    {
        var lf = RelativeLuminance(fg);
        var lb = RelativeLuminance(bg);
        var (light, dark) = lf >= lb ? (lf, lb) : (lb, lf);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Rgba c)
    {
        var r = Linearize(c.R / 255.0);
        var g = Linearize(c.G / 255.0);
        var b = Linearize(c.B / 255.0);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Linearize(double srgb) =>
        srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
}
