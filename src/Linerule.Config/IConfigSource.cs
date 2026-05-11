using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// Loads a <see cref="UserConfig"/> from a path. The interface seam is
/// <see cref="ValueTask"/>-shaped so the in-process sync implementation
/// (<see cref="InProcessConfigSource"/>) pays no allocation, while a future
/// out-of-process Rust-validator implementation (PR 3) can <c>await</c> the
/// subprocess naturally without changing call sites.
/// </summary>
public interface IConfigSource
{
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "In-process implementations delegate to ConfigLoader, which uses Tomlyn reflection — see ADR-0010."
    )]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "In-process implementations delegate to ConfigLoader, which uses Tomlyn reflection — see ADR-0010."
    )]
    ValueTask<Result<UserConfig, ConfigError>> LoadAsync(string path, CancellationToken cancellationToken = default);
}
