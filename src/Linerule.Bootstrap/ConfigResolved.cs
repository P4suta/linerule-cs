namespace Linerule.Bootstrap;

/// <summary>
/// The user config has been loaded and validated; <see cref="Warnings"/>
/// surfaces any non-fatal diagnostics that survived the
/// <c>DiagnosticBag</c> filter.
/// </summary>
public sealed record ConfigResolved(
    object Config, // UserConfig — typed at call site to keep this assembly leaf-light
    System.Collections.Immutable.ImmutableArray<object> Warnings
) : CapabilityToken;
