namespace Linerule.Core;

/// <summary>
/// Domain validation errors raised by newtype constructors.
/// Closed sum: every variant is a sealed record; external derivation is impossible.
/// </summary>
public abstract record CoreError
{
    private protected CoreError() { }

    /// <summary>Opacity outside the valid <c>1..=255</c> range.</summary>
    public sealed record Opacity(int Given) : CoreError;

    /// <summary>Thickness outside the valid <c>1..=512</c> range.</summary>
    public sealed record Thickness(int Given) : CoreError;

    public string ToHumanString() => this switch
    {
        Opacity o => $"opacity must be in 1..=255 (got {o.Given})",
        Thickness t => $"thickness must be in 1..=512 (got {t.Given})",
        _ => throw new System.Diagnostics.UnreachableException(),
    };
}
