using System.Collections.Immutable;

namespace Linerule.Config;

/// <summary>
/// Error returned by the config loader. Closed sum: every variant is a
/// sealed record with a focused payload.
/// </summary>
public abstract record ConfigError
{
    private protected ConfigError() { }

    /// <summary>Filesystem error before parsing began (file missing, IO failure).</summary>
    public sealed record FileSystem(string Path, string Reason) : ConfigError;

    /// <summary>One or more diagnostics raised by the parser or schema validator.</summary>
    public sealed record SchemaDiagnostics(ImmutableArray<ConfigDiagnostic> Items) : ConfigError;
}
