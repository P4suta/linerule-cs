using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Stage 4 of the config pipeline: turn the untrusted <see cref="RawUserConfig"/>
/// into a fully-validated <see cref="UserConfig"/>, or aggregate the diagnostics
/// explaining why it cannot.
///
/// <para>
/// Each section is validated as a <see cref="Validation{T}"/> — independent
/// branches run to completion and their <see cref="DiagnosticBag"/> errors
/// accumulate (applicative semantics) so the user sees every problem at once.
/// Per-section unknown-key reporting lives inside each section's validator via
/// <see cref="UnknownKeyValidator.WithUnknownKeys"/>; cross-field invariants
/// (HUD padding fit, contrast ratio, hotkey chord uniqueness, …) live in
/// <see cref="CrossFieldValidator"/> and operate on the already-resolved
/// section values.
/// </para>
/// </summary>
internal static class Validator
{
    private static readonly string[] KnownTopLevel = ["overlay", "hotkeys", "input", "hud", "render"];

    public static Result<UserConfig, ConfigError> Validate(
        RawUserConfig raw,
        string? source,
        IReadOnlyList<ConfigDiagnostic>? carry = null
    )
    {
        ArgumentNullException.ThrowIfNull(raw);

        var carryBag = SeedBag(carry);
        var topUnknown = UnknownKeyValidator.ForSection(raw.UnknownTopLevelKeys, string.Empty, source, KnownTopLevel);

        var sections = Validation.Apply5(
            ValidateOverlay(raw.Overlay, source),
            ValidateHotkeys(raw.Hotkeys, source),
            ValidateInput(raw.Input, source),
            ValidateHud(raw.Hud, source),
            ValidateRender(raw.Render, source),
            (o, h, i, hd, r) => new UserConfig(o, h, i, hd, r)
        );

        // Cross-field runs on the resolved value (fallbacks are baked in for
        // any section that produced an Error — see Validation<T> contract).
        var crossBag = CrossFieldValidator.Run(
            sections.Value.Overlay,
            sections.Value.Hotkeys,
            sections.Value.Input,
            sections.Value.Hud,
            sections.Value.Render,
            source
        );

        var finalBag = DiagnosticBag.Combine(
            DiagnosticBag.Combine(carryBag, topUnknown),
            DiagnosticBag.Combine(sections.Errors, crossBag)
        );

        return finalBag.IsFatal
            ? Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([.. finalBag.Build()]))
            : Result.Ok<UserConfig, ConfigError>(sections.Value);
    }

    private static DiagnosticBag SeedBag(IReadOnlyList<ConfigDiagnostic>? carry)
    {
        if (carry is null or { Count: 0 })
        {
            return DiagnosticBag.Empty;
        }
        var bag = DiagnosticBag.Empty;
        bag.AddRange(carry);
        return bag;
    }

    // ----- Section validators ---------------------------------------------

    private static Validation<OverlayConfig> ValidateOverlay(RawOverlayConfig raw, string? source)
    {
        var def = OverlayConfig.Default;
        var bag = DiagnosticBag.Empty;
        var mask = raw.MaskColor ?? def.MaskColor;

        var thickness = raw.Thickness switch
        {
            null => def.Thickness,
            int v => Thickness.TryCreate(v) switch
            {
                Result<Thickness, CoreError>.Ok ok => ok.Value,
                Result<Thickness, CoreError>.Err err => RejectAndDefault(
                    bag,
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
                    bag,
                    source,
                    "overlay.opacity",
                    err.Error,
                    def.Opacity
                ),
                _ => def.Opacity,
            },
        };

        return new Validation<OverlayConfig>(new OverlayConfig(mask, thickness, opacity), bag).WithUnknownKeys(
            raw.UnknownKeys,
            "overlay",
            source
        );
    }

    private static T RejectAndDefault<T>(DiagnosticBag bag, string? source, string dotPath, CoreError error, T fallback)
    {
        bag.Add(
            new ConfigDiagnostic(
                Message: error.ToHumanString(),
                Source: source,
                Span: null,
                Severity: DiagnosticSeverity.Error,
                DotPath: dotPath,
                Cause: error
            )
        );
        return fallback;
    }

    private static Validation<HotkeyMap> ValidateHotkeys(RawHotkeyMap raw, string? source)
    {
        var def = HotkeyMap.Default;
        var resolved = new HotkeyMap(
            CycleMode: raw.CycleMode ?? def.CycleMode,
            ToggleVisible: raw.ToggleVisible ?? def.ToggleVisible,
            Thicker: raw.Thicker ?? def.Thicker,
            Thinner: raw.Thinner ?? def.Thinner,
            MoreOpaque: raw.MoreOpaque ?? def.MoreOpaque,
            LessOpaque: raw.LessOpaque ?? def.LessOpaque,
            Quit: raw.Quit ?? def.Quit
        );
        return Validation.Ok(resolved).WithUnknownKeys(raw.UnknownKeys, "hotkeys", source);
    }

    private static Validation<InputConfig> ValidateInput(RawInputConfig raw, string? source)
    {
        var tap = ValidateTapStep(raw.TapStep, source);
        var rep = ValidateRepeat(raw.Repeat, source);
        return Validation
            .Apply2(tap, rep, (t, r) => new InputConfig(t, r))
            .WithUnknownKeys(raw.UnknownKeys, "input", source);
    }

    private static Validation<TapStepConfig> ValidateTapStep(RawTapStepConfig? raw, string? source)
    {
        var def = TapStepConfig.Default;
        return raw is null
            ? Validation.Ok(def)
            : Validation
                .Apply2(
                    RangeValidator.InRange(raw.Thickness, def.Thickness, "input.tap_step.thickness", 1, 256, source),
                    RangeValidator.InRange(raw.Opacity, def.Opacity, "input.tap_step.opacity", 1, 256, source),
                    (t, o) => new TapStepConfig(t, o)
                )
                .WithUnknownKeys(raw.UnknownKeys, "input.tap_step", source);
    }

    private static Validation<RepeatConfig> ValidateRepeat(RawRepeatConfig? raw, string? source)
    {
        var def = RepeatConfig.Default;
        return raw is null
            ? Validation.Ok(def)
            : Validation
                .Apply4(
                    RangeValidator.InRange(
                        raw.InitialDelayMs,
                        def.InitialDelayMs,
                        "input.repeat.initial_delay_ms",
                        50,
                        2000,
                        source
                    ),
                    RangeValidator.InRange(
                        raw.LongPressThresholdMs,
                        def.LongPressThresholdMs,
                        "input.repeat.long_press_threshold_ms",
                        50,
                        2000,
                        source
                    ),
                    RangeValidator.InRange(
                        raw.SlowRepeatIntervalMs,
                        def.SlowRepeatIntervalMs,
                        "input.repeat.slow_repeat_interval_ms",
                        50,
                        2000,
                        source
                    ),
                    RangeValidator.InRange(
                        raw.ReleasePollMs,
                        def.ReleasePollMs,
                        "input.repeat.release_poll_ms",
                        16,
                        200,
                        source
                    ),
                    (initial, threshold, slow, release) => new RepeatConfig(initial, threshold, slow, release)
                )
                .WithUnknownKeys(raw.UnknownKeys, "input.repeat", source);
    }

    private static Validation<HudConfig> ValidateHud(RawHudConfig raw, string? source)
    {
        var def = HudConfig.Default;
        return Validation
            .Apply7(
                RangeValidator.InRange(raw.BaseOpacity, def.BaseOpacity, "hud.base_opacity", 0f, 1f, source),
                RangeValidator.InRange(raw.FadeDecayPx, def.FadeDecayPx, "hud.fade_decay_px", 1f, 1000f, source),
                RangeValidator.InRange(
                    raw.TelemetryRefreshMs,
                    def.TelemetryRefreshMs,
                    "hud.telemetry_refresh_ms",
                    50L,
                    2000L,
                    source
                ),
                ValidateHudGeometry(raw, def.Geometry, source),
                ValidateHudPadding(raw.Padding, def.Padding, source),
                ValidateHudFonts(raw.Fonts, def.Fonts, source),
                ValidateHudColors(raw.Colors, def.Colors, source),
                (op, fd, tm, ge, pa, fn, co) => new HudConfig(op, fd, tm, ge, pa, fn, co)
            )
            .WithUnknownKeys(raw.UnknownKeys, "hud", source);
    }

    private static Validation<HudGeometry> ValidateHudGeometry(RawHudConfig raw, HudGeometry def, string? source) =>
        Validation.Apply3(
            RangeValidator.InRange(raw.WidthLogical, def.WidthLogical, "hud.width_logical", 1f, 10000f, source),
            RangeValidator.InRange(raw.HeightLogical, def.HeightLogical, "hud.height_logical", 1f, 10000f, source),
            RangeValidator.InRange(raw.MarginLogical, def.MarginLogical, "hud.margin_logical", 0f, 10000f, source),
            (w, h, m) => new HudGeometry(w, h, m)
        );

    private static Validation<HudPadding> ValidateHudPadding(RawHudPadding? raw, HudPadding def, string? source)
    {
        return raw is null
            ? Validation.Ok(def)
            : Validation
                .Apply3(
                    RangeValidator.InRange(raw.Edge, def.Edge, "hud.padding.edge", 0f, 1000f, source),
                    RangeValidator.InRange(raw.Section, def.Section, "hud.padding.section", 0f, 1000f, source),
                    RangeValidator.InRange(raw.Row, def.Row, "hud.padding.row", 0f, 1000f, source),
                    (e, s, r) => new HudPadding(e, s, r)
                )
                .WithUnknownKeys(raw.UnknownKeys, "hud.padding", source);
    }

    private static Validation<HudFonts> ValidateHudFonts(RawHudFonts? raw, HudFonts def, string? source)
    {
        if (raw is null)
        {
            return Validation.Ok(def);
        }
        var titleFamily = raw.TitleFamily ?? def.TitleFamily;
        var monoFamily = raw.MonoFamily ?? def.MonoFamily;
        return Validation
            .Apply4(
                RangeValidator.InRange(raw.Title, def.Title, "hud.fonts.title", 1f, 200f, source),
                RangeValidator.InRange(raw.Status, def.Status, "hud.fonts.status", 1f, 200f, source),
                RangeValidator.InRange(raw.Body, def.Body, "hud.fonts.body", 1f, 200f, source),
                RangeValidator.InRange(raw.Telemetry, def.Telemetry, "hud.fonts.telemetry", 1f, 200f, source),
                (title, status, body, telemetry) =>
                    new HudFonts(title, status, body, telemetry, titleFamily, monoFamily)
            )
            .WithUnknownKeys(raw.UnknownKeys, "hud.fonts", source);
    }

    private static Validation<HudColors> ValidateHudColors(RawHudColors? raw, HudColors def, string? source)
    {
        if (raw is null)
        {
            return Validation.Ok(def);
        }
        var resolved = new HudColors(
            Background: raw.Background ?? def.Background,
            Foreground: raw.Foreground ?? def.Foreground,
            Subtle: raw.Subtle ?? def.Subtle,
            Accent: raw.Accent ?? def.Accent,
            Hint: raw.Hint ?? def.Hint,
            Divider: raw.Divider ?? def.Divider
        );
        return Validation.Ok(resolved).WithUnknownKeys(raw.UnknownKeys, "hud.colors", source);
    }

    private static Validation<RenderConfig> ValidateRender(RawRenderConfig raw, string? source)
    {
        var def = RenderConfig.Default;
        return Validation
            .Apply2(
                RangeValidator.InRange(raw.WarnRatio, def.WarnRatio, "render.warn_ratio", 0.05, 1.5, source),
                RangeValidator.InRange(
                    raw.FallbackRefreshHz,
                    def.FallbackRefreshHz,
                    "render.fallback_refresh_hz",
                    1,
                    480,
                    source
                ),
                (warn, hz) => new RenderConfig(warn, hz)
            )
            .WithUnknownKeys(raw.UnknownKeys, "render", source);
    }
}
