using System.Collections.Immutable;
using System.Globalization;
using Linerule.Core;
using Tomlyn;
using Tomlyn.Model;

namespace Linerule.Config;

/// <summary>
/// Loader / parser for <c>%APPDATA%\linerule\config.toml</c>.
/// Uses Tomlyn 2.x' <see cref="TomlSerializer"/> API to deserialize into the
/// untyped DOM (<see cref="TomlTable"/>) and walks it manually so domain
/// validation (Opacity / Thickness / Rgba) flows through the same
/// <see cref="Result{T,E}"/> pipeline as the rest of Core.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Default config path: <c>%APPDATA%\linerule\config.toml</c> on Windows,
    /// <c>$XDG_CONFIG_HOME/linerule/config.toml</c> on POSIX (Core/Config target net10.0).
    /// </summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "linerule", "config.toml");
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Delegates to ParseString — see ADR-0010.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Delegates to ParseString — see ADR-0010.")]
    public static Result<UserConfig, ConfigError> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or DirectoryNotFoundException
                        or FileNotFoundException
            )
        {
            return Result.Err<UserConfig, ConfigError>(new ConfigError.FileSystem(path, ex.Message));
        }

        return ParseString(text, path);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Tomlyn 2.x's reflection-based TomlSerializer.Deserialize<TomlTable> is the only public API for the untyped DOM. "
            + "ADR-0010 documents that the TOML loader is opted out of trim/AOT analysis until Tomlyn ships a source-generated TomlTable context."
    )]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Tomlyn 2.x reflection deserialization. See ADR-0010.")]
    public static Result<UserConfig, ConfigError> ParseString(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        TomlTable? model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(text);
        }
        catch (TomlException ex)
        {
            var diag = new ConfigDiagnostic(Message: ex.Message, Source: sourcePath, Span: ExtractFirstSpan(ex));
            return Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([diag]));
        }

        if (model is null)
        {
            return Result.Ok<UserConfig, ConfigError>(UserConfig.Default);
        }

        var diagnostics = new List<ConfigDiagnostic>();
        var overlay = ReadOverlay(model, sourcePath, diagnostics);
        var hotkeys = ReadHotkeys(model, sourcePath, diagnostics);

        return diagnostics.Count > 0
            ? Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([.. diagnostics]))
            : Result.Ok<UserConfig, ConfigError>(new UserConfig(overlay, hotkeys));
    }

    private static SourcePosition? ExtractFirstSpan(TomlException ex)
    {
        var first = ex.Diagnostics.FirstOrDefault();
        return first is null ? null : new SourcePosition(first.Span.Start.Line + 1, first.Span.Start.Column + 1);
    }

    private static OverlayConfig ReadOverlay(TomlTable root, string? source, List<ConfigDiagnostic> diags)
    {
        if (!TryGetTable(root, "overlay", out var overlay))
        {
            return OverlayConfig.Default;
        }

        var mask = ReadRgba(overlay, "mask_color", source, diags) ?? Rgba.DefaultMask;
        var thickness = ReadValidated(
            overlay,
            "thickness",
            source,
            diags,
            v => Thickness.TryCreate((int)v),
            Thickness.Default
        );
        var opacity = ReadValidated(overlay, "opacity", source, diags, v => Opacity.TryCreate((int)v), Opacity.Default);

        return new OverlayConfig(mask, thickness, opacity);
    }

    private static HotkeyMap ReadHotkeys(TomlTable root, string? source, List<ConfigDiagnostic> diags)
    {
        return !TryGetTable(root, "hotkeys", out var hk)
            ? HotkeyMap.Default
            : new HotkeyMap(
                CycleMode: ReadString(hk, "cycle_mode", HotkeyMap.Default.CycleMode, source, diags),
                ToggleVisible: ReadString(hk, "toggle_visible", HotkeyMap.Default.ToggleVisible, source, diags),
                Thicker: ReadString(hk, "thicker", HotkeyMap.Default.Thicker, source, diags),
                Thinner: ReadString(hk, "thinner", HotkeyMap.Default.Thinner, source, diags),
                MoreOpaque: ReadString(hk, "more_opaque", HotkeyMap.Default.MoreOpaque, source, diags),
                LessOpaque: ReadString(hk, "less_opaque", HotkeyMap.Default.LessOpaque, source, diags),
                Quit: ReadString(hk, "quit", HotkeyMap.Default.Quit, source, diags)
            );
    }

    private static Rgba? ReadRgba(TomlTable parent, string key, string? source, List<ConfigDiagnostic> diags)
    {
        if (!parent.TryGetValue(key, out var raw))
        {
            return null;
        }

        if (raw is not TomlTable t)
        {
            diags.Add(new ConfigDiagnostic($"`{key}` must be an inline table {{ r, g, b, a }}", source, Span: null));
            return null;
        }

        var r = ReadByte(t, "r", source, diags);
        var g = ReadByte(t, "g", source, diags);
        var b = ReadByte(t, "b", source, diags);
        var a = ReadByte(t, "a", source, diags);
        return new Rgba(r, g, b, a);
    }

    private static byte ReadByte(TomlTable t, string key, string? source, List<ConfigDiagnostic> diags)
    {
        if (!t.TryGetValue(key, out var v))
        {
            diags.Add(new ConfigDiagnostic($"missing `{key}`", source, Span: null));
            return 0;
        }

        if (v is not long l || l is < 0 or > 255)
        {
            diags.Add(
                new ConfigDiagnostic($"`{key}` must be an integer in 0..=255 (got {Stringify(v)})", source, Span: null)
            );
            return 0;
        }

        return (byte)l;
    }

    private static T ReadValidated<T>(
        TomlTable parent,
        string key,
        string? source,
        List<ConfigDiagnostic> diags,
        Func<long, Result<T, CoreError>> validate,
        T fallback
    )
    {
        if (!parent.TryGetValue(key, out var v))
        {
            return fallback;
        }

        if (v is not long l)
        {
            diags.Add(
                new ConfigDiagnostic(
                    $"`{key}` must be an integer (got {v?.GetType().Name ?? "null"})",
                    source,
                    Span: null
                )
            );
            return fallback;
        }

        return validate(l) switch
        {
            Result<T, CoreError>.Ok ok => ok.Value,
            Result<T, CoreError>.Err err => Reject(err.Error, key, source, diags, fallback),
            _ => fallback,
        };
    }

    private static T Reject<T>(CoreError error, string key, string? source, List<ConfigDiagnostic> diags, T fallback)
    {
        diags.Add(new ConfigDiagnostic($"`{key}`: {error.ToHumanString()}", source, Span: null));
        return fallback;
    }

    private static string ReadString(
        TomlTable parent,
        string key,
        string fallback,
        string? source,
        List<ConfigDiagnostic> diags
    )
    {
        if (!parent.TryGetValue(key, out var v))
        {
            return fallback;
        }

        if (v is not string s)
        {
            diags.Add(
                new ConfigDiagnostic(
                    $"`{key}` must be a string (got {v?.GetType().Name ?? "null"})",
                    source,
                    Span: null
                )
            );
            return fallback;
        }

        return s;
    }

    private static bool TryGetTable(TomlTable parent, string key, out TomlTable table)
    {
        if (parent.TryGetValue(key, out var raw) && raw is TomlTable t)
        {
            table = t;
            return true;
        }

        table = null!;
        return false;
    }

    private static string Stringify(object? v) =>
        v switch
        {
            null => "null",
            bool b => b.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            string s => $"\"{s}\"",
            _ => v.GetType().Name,
        };
}
