using System.Globalization;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Cross-field invariants that span more than one section of
/// <see cref="UserConfig"/>: HUD geometry must accommodate padding, repeat
/// timings must be consistent, foreground/background must be distinguishable,
/// hotkey chords must be unique, render budget must leave headroom. Each
/// check is a pure function from already-resolved sections to a
/// <see cref="DiagnosticBag"/> contribution; the public <see cref="Run"/>
/// entry point composes them via monoid append.
/// </summary>
internal static class CrossFieldValidator
{
    public static DiagnosticBag Run(
        OverlayConfig overlay,
        HotkeyMap hotkeys,
        InputConfig input,
        HudConfig hud,
        RenderConfig render,
        string? source
    )
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(render);
        _ = overlay;
        var bag = DiagnosticBag.Empty;
        CheckHudPaddingFitsWidth(hud, source, bag);
        CheckHudPaddingFitsHeight(hud, source, bag);
        CheckRepeatInitialDelayVsThreshold(input, source, bag);
        CheckRepeatSlowVsRelease(input, source, bag);
        CheckForegroundBackgroundDistinct(hud, source, bag);
        CheckHotkeyChordsUnique(hotkeys, source, bag);
        CheckRenderWarnRatioSane(render, source, bag);
        return bag;
    }

    /// <summary>HUD width must accommodate edge padding on both sides.</summary>
    private static void CheckHudPaddingFitsWidth(HudConfig hud, string? source, DiagnosticBag bag)
    {
        if (hud.Geometry.WidthLogical < 2f * hud.Padding.Edge)
        {
            bag.Add(
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
    private static void CheckHudPaddingFitsHeight(HudConfig hud, string? source, DiagnosticBag bag)
    {
        if (hud.Geometry.HeightLogical < 2f * hud.Padding.Edge)
        {
            bag.Add(
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
    private static void CheckRepeatInitialDelayVsThreshold(InputConfig input, string? source, DiagnosticBag bag)
    {
        if (input.Repeat.InitialDelayMs < input.Repeat.LongPressThresholdMs)
        {
            bag.Add(
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
    private static void CheckRepeatSlowVsRelease(InputConfig input, string? source, DiagnosticBag bag)
    {
        if (input.Repeat.SlowRepeatIntervalMs < input.Repeat.ReleasePollMs)
        {
            bag.Add(
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
    private static void CheckForegroundBackgroundDistinct(HudConfig hud, string? source, DiagnosticBag bag)
    {
        if (ColorContrast.RgbEqual(hud.Colors.Foreground, hud.Colors.Background))
        {
            bag.Add(
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
        CheckContrastRatio(hud, source, bag);
    }

    /// <summary>WCAG simple contrast ratio &lt; 3.0 ⇒ Warning.</summary>
    private static void CheckContrastRatio(HudConfig hud, string? source, DiagnosticBag bag)
    {
        var ratio = ColorContrast.ContrastRatio(hud.Colors.Foreground, hud.Colors.Background);
        if (ratio < 3.0)
        {
            bag.Add(
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

    /// <summary>Duplicate hotkey chord assignments produce an Error per duplicated chord.</summary>
    private static void CheckHotkeyChordsUnique(HotkeyMap hotkeys, string? source, DiagnosticBag bag)
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
                bag.Add(
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

    /// <summary>Render warn ratio dangerously high → spurious warnings at runtime.</summary>
    private static void CheckRenderWarnRatioSane(RenderConfig render, string? source, DiagnosticBag bag)
    {
        if (render.WarnRatio > 0.95)
        {
            bag.Add(
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
}
