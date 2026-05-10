namespace Linerule.Config;

/// <summary>
/// A single diagnostic raised while loading a config file. Carries an optional
/// 1-based line/column span so CLI output can underline the offending source range.
/// </summary>
public sealed record ConfigDiagnostic(string Message, string? Source, SourcePosition? Span)
{
    public override string ToString() =>
        Span is { } s ? $"{Source ?? "<config>"}:{s.Line}:{s.Column}: {Message}" : $"{Source ?? "<config>"}: {Message}";
}
