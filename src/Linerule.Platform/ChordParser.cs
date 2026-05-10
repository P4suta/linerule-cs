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
    private static readonly FrozenSet<string> CtrlAliases = new[] { "ctrl", "control" }.ToFrozenSet(
        StringComparer.Ordinal
    );
    private static readonly FrozenSet<string> AltAliases = new[] { "alt", "option", "opt" }.ToFrozenSet(
        StringComparer.Ordinal
    );
    private static readonly FrozenSet<string> ShiftAliases = new[] { "shift" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> MetaAliases = new[] { "meta", "win", "super", "cmd" }.ToFrozenSet(
        StringComparer.Ordinal
    );

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

            if (TryConsumeModifier(raw, ref ctrl, ref alt, ref shift, ref meta))
            {
                continue;
            }

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

        return key is null
            ? Result.Err<ChordSpec, ChordError>(new ChordError.NoKey())
            : Result.Ok<ChordSpec, ChordError>(new ChordSpec(new Modifiers(ctrl, alt, shift, meta), key));
    }

    /// <summary>
    /// Match <paramref name="raw"/> against the modifier alias sets and
    /// flip the corresponding flag. Returns <see langword="true"/> if the
    /// token was consumed as a modifier; <see langword="false"/> means
    /// the caller should try to parse it as a key.
    /// </summary>
    private static bool TryConsumeModifier(string raw, ref bool ctrl, ref bool alt, ref bool shift, ref bool meta)
    {
        var lower = raw.ToLowerInvariant();
        if (CtrlAliases.Contains(lower))
        {
            ctrl = true;
            return true;
        }
        if (AltAliases.Contains(lower))
        {
            alt = true;
            return true;
        }
        if (ShiftAliases.Contains(lower))
        {
            shift = true;
            return true;
        }
        if (MetaAliases.Contains(lower))
        {
            meta = true;
            return true;
        }
        return false;
    }

    private static KeyCode? TryParseKey(string token)
    {
        if (token.Length == 1)
        {
            var c = token[0];
            return c switch
            {
                >= 'A' and <= 'Z' => new KeyCode.Letter((byte)c),
                >= 'a' and <= 'z' => new KeyCode.Letter((byte)char.ToUpperInvariant(c)),
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
