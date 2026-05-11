using System.Globalization;
using System.Numerics;

namespace Linerule.Config;

/// <summary>
/// Generic range check over <see cref="INumber{T}"/>. Replaces the four
/// hand-written <c>CheckIntRange</c> / <c>CheckLongRange</c> /
/// <c>CheckFloatRange</c> / <c>CheckDoubleRange</c> copies in
/// <c>Validator</c> with one polymorphic implementation that handles
/// signed/unsigned integers and IEEE-754 floats uniformly (NaN is treated as
/// out of range).
/// </summary>
public static class RangeValidator
{
    /// <summary>
    /// Validate <paramref name="value"/> against [<paramref name="min"/>,
    /// <paramref name="max"/>]. <see langword="null"/> inputs silently fall back to
    /// <paramref name="fallback"/> (the caller's documented default).
    /// Out-of-range or NaN inputs produce an Error-severity
    /// <see cref="ConfigDiagnostic"/> and the value collapses to
    /// <paramref name="fallback"/>.
    /// </summary>
    public static Validation<T> InRange<T>(T? value, T fallback, string dotPath, T min, T max, string? source)
        where T : struct, INumber<T>
    {
        if (value is null)
        {
            return Validation.Ok(fallback);
        }

        var v = value.Value;
        if (T.IsNaN(v) || v < min || v > max)
        {
            var message = $"{v} is out of range [{min}, {max}]";
            var suggestion = $"Set a value between {min} and {max}; the default is {fallback}.";
            var diagnostic = new ConfigDiagnostic(
                Message: message,
                Source: source,
                Span: null,
                Severity: DiagnosticSeverity.Error,
                DotPath: dotPath,
                Suggestion: suggestion
            );
            return new Validation<T>(fallback, DiagnosticBag.Of(diagnostic));
        }

        return new Validation<T>(v, DiagnosticBag.Empty);
    }
}
