using System.Collections.Frozen;
using Linerule.Core;

namespace Linerule.Platform;

/// <summary>
/// Cross-platform parser for chord strings like <c>"Ctrl+Alt+R"</c>.
/// Whitespace around <c>+</c> is allowed; tokens are case-insensitive.
/// Mirrors the Rust <c>linerule-platform/src/chord.rs</c>.
/// </summary>
public static class ChordParser
{
    private static readonly FrozenSet<string> CtrlAliases =
        new[] { "ctrl", "control" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> AltAliases =
        new[] { "alt", "option", "opt" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> ShiftAliases =
        new[] { "shift" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> MetaAliases =
        new[] { "meta", "win", "super", "cmd" }.ToFrozenSet(StringComparer.Ordinal);

    public static Result<ChordSpec, ChordError> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result.Err<ChordSpec, ChordError>(new ChordError.Empty());
        }

        var parts = input.Split('+');
        var ctrl = false;
        var alt = false;
        var shift = false;
        var meta = false;
        KeyCode? key = null;
        string? keyToken = null;

        for (var i = 0; i < parts.Length; i++)
        {
            var raw = parts[i].Trim();
            if (raw.Length == 0)
            {
                return Result.Err<ChordSpec, ChordError>(new ChordError.EmptyToken(i));
            }

            var lower = raw.ToLowerInvariant();
            if (CtrlAliases.Contains(lower)) { ctrl = true; continue; }
            if (AltAliases.Contains(lower)) { alt = true; continue; }
            if (ShiftAliases.Contains(lower)) { shift = true; continue; }
            if (MetaAliases.Contains(lower)) { meta = true; continue; }

            var thisKey = TryParseKey(raw);
            if (thisKey is null)
            {
                return Result.Err<ChordSpec, ChordError>(new ChordError.UnknownPart(raw));
            }

            if (key is not null)
            {
                return Result.Err<ChordSpec, ChordError>(new ChordError.MultipleKeys(keyToken!, raw));
            }

            key = thisKey;
            keyToken = raw;
        }

        if (key is null)
        {
            return Result.Err<ChordSpec, ChordError>(new ChordError.NoKey());
        }

        return Result.Ok<ChordSpec, ChordError>(new ChordSpec(new Modifiers(ctrl, alt, shift, meta), key));
    }

    private static KeyCode? TryParseKey(string token)
    {
        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= 'A' and <= 'Z')
            {
                return new KeyCode.Letter((byte)c);
            }
            if (c is >= 'a' and <= 'z')
            {
                return new KeyCode.Letter((byte)char.ToUpperInvariant(c));
            }
            return c switch
            {
                '[' => KeyCode.BracketLeft.Instance,
                ']' => KeyCode.BracketRight.Instance,
                '-' => KeyCode.Minus.Instance,
                '=' => KeyCode.Equal.Instance,
                _ => null,
            };
        }

        return token.ToLowerInvariant() switch
        {
            "up" or "arrowup" => KeyCode.ArrowUp.Instance,
            "down" or "arrowdown" => KeyCode.ArrowDown.Instance,
            "left" or "arrowleft" => KeyCode.ArrowLeft.Instance,
            "right" or "arrowright" => KeyCode.ArrowRight.Instance,
            "bracketleft" => KeyCode.BracketLeft.Instance,
            "bracketright" => KeyCode.BracketRight.Instance,
            "minus" => KeyCode.Minus.Instance,
            "equal" or "equals" => KeyCode.Equal.Instance,
            _ => null,
        };
    }
}
