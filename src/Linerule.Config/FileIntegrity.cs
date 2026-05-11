using System.Security.Cryptography;
using System.Text;
using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// File-layer integrity for the user's <c>config.toml</c>:
/// <list type="number">
///   <item>Size cap — reject files larger than <see cref="MaxConfigBytes"/> (1 MB).</item>
///   <item>Strict UTF-8 decode — invalid byte sequences become a fatal diagnostic
///         instead of silently substituting replacement characters.</item>
///   <item>Optional SHA-256 sidecar — if a file named
///         <c>config.toml.sha256</c> sits next to the config, its content (hex digest)
///         is checked against the actual bytes. A mismatch surfaces as a non-fatal
///         <see cref="DiagnosticSeverity.Warning"/> ("the file was edited since
///         it was last signed"); a missing sidecar is silently OK (opt-in feature
///         via <c>linerule config sign</c>).</item>
/// </list>
///
/// <para>
/// Returned <see cref="FileIntegrityOk.Text"/> is the verified UTF-8 string. Any
/// non-fatal warnings (e.g. sidecar mismatch) ride along in
/// <see cref="FileIntegrityOk.Warnings"/> and are carried through to the
/// validator stage so the user sees one merged diagnostic report.
/// </para>
/// </summary>
public static class FileIntegrity
{
    /// <summary>Maximum allowed config file size in bytes. Anything larger is rejected.</summary>
    public const long MaxConfigBytes = 1 * 1024 * 1024;

    /// <summary>Filename extension appended to the config path to form the SHA-256 sidecar.</summary>
    public const string SidecarExtension = ".sha256";

    /// <summary>Result of a successful read: verified UTF-8 text plus any non-fatal warnings.</summary>
    public sealed record FileIntegrityOk(string Text, IReadOnlyList<ConfigDiagnostic> Warnings);

    /// <summary>
    /// Read the config at <paramref name="path"/> with file-layer integrity checks.
    /// <para>
    /// Implemented as a Result-monad Bind chain over the linear integrity pipeline:
    /// probe → size cap → bytes → strict UTF-8 → sidecar. Each stage owns one
    /// failure mode; the chain short-circuits on the first <c>Err</c>.
    /// </para>
    /// </summary>
    public static Result<FileIntegrityOk, ConfigError> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ProbeFile(path)
            .Bind(info => CheckSize(info, path))
            .Bind(info => ReadBytes(info, path))
            .Bind(state => DecodeUtf8(state, path))
            .Bind(state => VerifySidecar(state, path));
    }

    /// <summary>Resolve <paramref name="path"/> to a <see cref="FileInfo"/> and verify existence.</summary>
    private static Result<FileInfo, ConfigError> ProbeFile(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return Result.Err<FileInfo, ConfigError>(new ConfigError.FileSystem(path, ex.Message));
        }

        return !info.Exists
            ? Result.Err<FileInfo, ConfigError>(new ConfigError.FileSystem(path, "file not found"))
            : Result.Ok<FileInfo, ConfigError>(info);
    }

    /// <summary>Reject files larger than <see cref="MaxConfigBytes"/>.</summary>
    private static Result<FileInfo, ConfigError> CheckSize(FileInfo info, string path)
    {
        return info.Length <= MaxConfigBytes
            ? Result.Ok<FileInfo, ConfigError>(info)
            : Result.Err<FileInfo, ConfigError>(
                new ConfigError.SchemaDiagnostics([
                    new ConfigDiagnostic(
                        Message: string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"config file is {info.Length} bytes, exceeding the {MaxConfigBytes}-byte cap. A typical config is under 4 KB; an oversized file usually means accidental copy-paste of unrelated content."
                        ),
                        Source: path,
                        Span: null,
                        Severity: DiagnosticSeverity.Error,
                        DotPath: null,
                        Suggestion: "Inspect the file in a text editor; if needed, regenerate via `linerule config print-default`."
                    ),
                ])
            );
    }

    /// <summary>Read the raw bytes; map IO failures to <see cref="ConfigError.FileSystem"/>.</summary>
    private static Result<(FileInfo Info, byte[] Bytes), ConfigError> ReadBytes(FileInfo info, string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return Result.Ok<(FileInfo, byte[]), ConfigError>((info, bytes));
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or DirectoryNotFoundException
                        or FileNotFoundException
            )
        {
            return Result.Err<(FileInfo, byte[]), ConfigError>(new ConfigError.FileSystem(path, ex.Message));
        }
    }

    /// <summary>Strictly decode bytes as UTF-8; invalid sequences become a fatal diagnostic.</summary>
    private static Result<(FileInfo Info, byte[] Bytes, string Text), ConfigError> DecodeUtf8(
        (FileInfo Info, byte[] Bytes) state,
        string path
    )
    {
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strict.GetString(state.Bytes);
            return Result.Ok<(FileInfo, byte[], string), ConfigError>((state.Info, state.Bytes, text));
        }
        catch (DecoderFallbackException ex)
        {
            return Result.Err<(FileInfo, byte[], string), ConfigError>(
                new ConfigError.SchemaDiagnostics([
                    new ConfigDiagnostic(
                        Message: $"config file is not valid UTF-8: {ex.Message}",
                        Source: path,
                        Span: null,
                        Severity: DiagnosticSeverity.Error,
                        DotPath: null,
                        Suggestion: "Re-save the file in your editor with UTF-8 encoding (no BOM)."
                    ),
                ])
            );
        }
    }

    /// <summary>Compare bytes against an optional <c>.sha256</c> sidecar; mismatch is a warning.</summary>
    private static Result<FileIntegrityOk, ConfigError> VerifySidecar(
        (FileInfo Info, byte[] Bytes, string Text) state,
        string path
    )
    {
        var warnings = new List<ConfigDiagnostic>();
        var sidecarPath = path + SidecarExtension;
        if (!File.Exists(sidecarPath))
        {
            return Result.Ok<FileIntegrityOk, ConfigError>(new FileIntegrityOk(state.Text, warnings));
        }

        string expected;
        try
        {
            expected = File.ReadAllText(sidecarPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            expected = string.Empty;
        }

        var actual = ComputeSha256Hex(state.Bytes);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                new ConfigDiagnostic(
                    Message: $"config SHA-256 mismatch with `{Path.GetFileName(sidecarPath)}`. Expected {Short(expected)}…, actual {Short(actual)}…. The file may have been edited since it was last signed.",
                    Source: path,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: null,
                    Suggestion: "Re-sign with `linerule config sign` after deliberate edits, or delete the sidecar if you don't need signing.",
                    Related: [sidecarPath]
                )
            );
        }

        return Result.Ok<FileIntegrityOk, ConfigError>(new FileIntegrityOk(state.Text, warnings));
    }

    /// <summary>Compute the lowercase hex SHA-256 of the given bytes.</summary>
    public static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Compute the lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static string ComputeSha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ComputeSha256Hex(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Write <paramref name="text"/> to <paramref name="path"/> and create a fresh
    /// SHA-256 sidecar at <paramref name="path"/> + <see cref="SidecarExtension"/>.
    /// </summary>
    public static void WriteWithSidecar(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(
            path + SidecarExtension,
            ComputeSha256Hex(text) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        );
    }

    private static string Short(string hex) => hex.Length >= 16 ? hex[..16] : hex;
}
