namespace Linerule.Config;

/// <summary>
/// Per-section combinator that converts a TOML "unknown keys" list into a
/// <see cref="DiagnosticBag"/> of <see cref="DiagnosticSeverity.Warning"/> diagnostics.
/// </summary>
internal static class UnknownKeyValidator
{
    /// <summary>
    /// Materialize an unknown-keys bag for one config section.
    /// <paramref name="dotPathPrefix"/> may be the empty string for the
    /// top-level table; in that case the key itself is the full dot-path.
    /// </summary>
    public static DiagnosticBag ForSection(
        IReadOnlyList<string> unknown,
        string dotPathPrefix,
        string? source,
        string[]? knownKeysHint = null
    )
    {
        ArgumentNullException.ThrowIfNull(unknown);
        if (unknown.Count == 0)
        {
            return DiagnosticBag.Empty;
        }

        var bag = DiagnosticBag.Empty;
        foreach (var key in unknown)
        {
            var full = string.IsNullOrEmpty(dotPathPrefix) ? key : $"{dotPathPrefix}.{key}";
            bag.Add(
                new ConfigDiagnostic(
                    Message: $"unknown key `{full}` — ignored",
                    Source: source,
                    Span: null,
                    Severity: DiagnosticSeverity.Warning,
                    DotPath: full,
                    Suggestion: knownKeysHint is { Length: > 0 }
                        ? $"Did you mean one of: {string.Join(", ", knownKeysHint)}?"
                        : "Check for typos against the documented schema."
                )
            );
        }
        return bag;
    }

    /// <summary>
    /// Fluent combinator: fold an unknown-keys bag into an existing
    /// <see cref="Validation{T}"/>. Warnings are concatenated via
    /// <see cref="DiagnosticBag.Combine"/>; the underlying value is preserved
    /// (warnings never demote an Ok validation).
    /// </summary>
    public static Validation<T> WithUnknownKeys<T>(
        this Validation<T> validation,
        IReadOnlyList<string> unknown,
        string dotPathPrefix,
        string? source,
        string[]? knownKeysHint = null
    )
    {
        ArgumentNullException.ThrowIfNull(unknown);
        var bag = ForSection(unknown, dotPathPrefix, source, knownKeysHint);
        if (bag.Count == 0)
        {
            return validation;
        }
        var merged = DiagnosticBag.Combine(validation.Errors ?? DiagnosticBag.Empty, bag);
        return new Validation<T>(validation.Value, merged);
    }
}
