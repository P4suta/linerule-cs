using Linerule.Core;
using Tomlyn;
using Tomlyn.Model;

namespace Linerule.Config;

/// <summary>
/// Loader / parser for <c>%APPDATA%\linerule\config.toml</c>.
///
/// <para><b>Pipeline (5 stages)</b>:</para>
/// <list type="number">
///   <item><see cref="FileIntegrity.Read"/> — size cap, UTF-8 strict decode, optional SHA-256 sidecar.</item>
///   <item><see cref="TomlSerializer.Deserialize{T}(string)"/> with <see cref="TomlTable"/> target — Tomlyn DOM.</item>
///   <item><see cref="RawConfigDeserializer.Deserialize"/> — DOM → untrusted <see cref="RawUserConfig"/>.</item>
///   <item><see cref="Validator.Validate"/> — range checks, smart-constructor invariants, cross-field invariants, unknown-key reporting.</item>
///   <item>Output: <see cref="Result{T,E}.Ok"/> with the typed <see cref="UserConfig"/>, or <see cref="Result{T,E}.Err"/> with aggregated <see cref="ConfigDiagnostic"/>s.</item>
/// </list>
///
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
        var integrity = FileIntegrity.Read(path);
        return integrity switch
        {
            Result<FileIntegrity.FileIntegrityOk, ConfigError>.Ok ok => ParseStringInternal(
                ok.Value.Text,
                path,
                ok.Value.Warnings
            ),
            Result<FileIntegrity.FileIntegrityOk, ConfigError>.Err err => Result.Err<UserConfig, ConfigError>(
                err.Error
            ),
            _ => Result.Err<UserConfig, ConfigError>(new ConfigError.FileSystem(path, "unreachable")),
        };
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Tomlyn reflection deserialization. See ADR-0010.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Tomlyn reflection deserialization. See ADR-0010.")]
    public static Result<UserConfig, ConfigError> ParseString(string text, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseStringInternal(text, sourcePath, carryWarnings: []);
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Tomlyn 2.x reflection deserialization. See ADR-0010.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Tomlyn 2.x reflection deserialization. See ADR-0010.")]
    private static Result<UserConfig, ConfigError> ParseStringInternal(
        string text,
        string? sourcePath,
        IReadOnlyList<ConfigDiagnostic> carryWarnings
    )
    {
        TomlTable? model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(text);
        }
        catch (TomlException ex)
        {
            var diag = new ConfigDiagnostic(
                Message: ex.Message,
                Source: sourcePath,
                Span: ExtractFirstSpan(ex),
                Severity: DiagnosticSeverity.Error
            );
            return Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([.. carryWarnings, diag]));
        }

        if (model is null || model.Count == 0)
        {
            return carryWarnings.Count > 0 && carryWarnings.Any(d => d.IsError)
                ? Result.Err<UserConfig, ConfigError>(new ConfigError.SchemaDiagnostics([.. carryWarnings]))
                : Result.Ok<UserConfig, ConfigError>(UserConfig.Default);
        }

        var raw = RawConfigDeserializer.Deserialize(model, sourcePath);
        return Validator.Validate(raw, sourcePath, carryWarnings);
    }

    private static SourcePosition? ExtractFirstSpan(TomlException ex)
    {
        var first = ex.Diagnostics.FirstOrDefault();
        return first is null ? null : new SourcePosition(first.Span.Start.Line + 1, first.Span.Start.Column + 1);
    }
}
