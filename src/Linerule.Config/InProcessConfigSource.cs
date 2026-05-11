using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// In-process C# implementation of <see cref="IConfigSource"/>. Delegates to
/// <see cref="ConfigLoader.Load"/> (FileIntegrity → Tomlyn parse →
/// RawConfigDeserializer → Validator). This is the always-available baseline;
/// PR 3 will add an external Rust binary swap behind the same interface.
/// </summary>
public sealed class InProcessConfigSource : IConfigSource
{
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Delegates to ConfigLoader.Load — see ADR-0010.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Delegates to ConfigLoader.Load — see ADR-0010.")]
    public ValueTask<Result<UserConfig, ConfigError>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<Result<UserConfig, ConfigError>>(ConfigLoader.Load(path));
    }
}
