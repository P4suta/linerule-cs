using System.Globalization;

namespace Linerule.Platform;

/// <summary>Errors raised by <see cref="ChordParser.Parse"/>.</summary>
public abstract record ChordError
{
    private protected ChordError() { }

    public sealed record Empty : ChordError;

    public sealed record EmptyToken(int Position) : ChordError;

    public sealed record UnknownPart(string Part) : ChordError;

    public sealed record MultipleKeys(string First, string Second) : ChordError;

    public sealed record NoKey : ChordError;

    public string ToHumanString() =>
        this switch
        {
            Empty => "chord is empty",
            EmptyToken e => string.Create(
                CultureInfo.InvariantCulture,
                $"empty token at position {e.Position} (consecutive '+'?)"
            ),
            UnknownPart u => $"unknown part: \"{u.Part}\"",
            MultipleKeys m => $"chord has multiple keys: \"{m.First}\" and \"{m.Second}\"",
            NoKey => "chord has only modifiers; missing the final key",
            _ => throw new System.Diagnostics.UnreachableException(),
        };
}
