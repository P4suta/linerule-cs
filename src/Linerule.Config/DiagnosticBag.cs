using System.Collections.Immutable;

namespace Linerule.Config;

/// <summary>
/// Append-only accumulator of <see cref="ConfigDiagnostic"/> values that
/// maintains the running join (⊔) over <see cref="SeverityLattice"/> in
/// O(1) per insertion. The pair
/// <c>(DiagnosticBag, <see cref="Combine"/>, <see cref="Empty"/>)</c> forms a
/// commutative monoid (associative, with <see cref="Empty"/> as identity),
/// so per-section validators can produce bags independently and combine them
/// without any ad-hoc merge logic.
///
/// <para>
/// <b>Why a class (not a struct)</b>: mutability is the point. Validators
/// produce bags incrementally — capturing the bag by reference and adding
/// to it keeps the call sites uncluttered. Reads remain safe through
/// <see cref="Build"/>, which returns an immutable snapshot.
/// </para>
/// </summary>
public sealed class DiagnosticBag
{
    private readonly ImmutableArray<ConfigDiagnostic>.Builder _items = ImmutableArray.CreateBuilder<ConfigDiagnostic>();

    /// <summary>Current join (⊔) over inserted diagnostics. Starts at <see cref="SeverityLattice.Bottom"/>.</summary>
    public DiagnosticSeverity Severity { get; private set; } = SeverityLattice.Bottom;

    /// <summary>Number of diagnostics accumulated so far.</summary>
    public int Count => _items.Count;

    /// <summary><see langword="true"/> iff <see cref="Severity"/> has reached <see cref="DiagnosticSeverity.Error"/>.</summary>
    public bool IsFatal => Severity >= DiagnosticSeverity.Error;

    /// <summary>Add a diagnostic; updates <see cref="Severity"/> via <see cref="SeverityLattice.Join"/>.</summary>
    public void Add(ConfigDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _items.Add(diagnostic);
        Severity = SeverityLattice.Join(Severity, diagnostic.Severity);
    }

    /// <summary>Bulk-insert; equivalent to a fold of <see cref="Add"/>.</summary>
    public void AddRange(IEnumerable<ConfigDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        foreach (var d in diagnostics)
        {
            Add(d);
        }
    }

    /// <summary>Immutable snapshot of the diagnostics inserted so far, in insertion order.</summary>
    public ImmutableArray<ConfigDiagnostic> Build() => _items.ToImmutable();

    /// <summary>Identity element of <see cref="Combine"/>.</summary>
    public static DiagnosticBag Empty => new();

    /// <summary>
    /// Construct a bag containing a single diagnostic. Convenience for
    /// the applicative-style error path used in <c>Validation&lt;T&gt;</c>.
    /// </summary>
    public static DiagnosticBag Of(ConfigDiagnostic diagnostic)
    {
        var bag = new DiagnosticBag();
        bag.Add(diagnostic);
        return bag;
    }

    /// <summary>
    /// Monoid operation: returns a fresh bag containing the concatenation of
    /// <paramref name="a"/> and <paramref name="b"/>. Associative; identity
    /// is <see cref="Empty"/>.
    /// </summary>
    public static DiagnosticBag Combine(DiagnosticBag a, DiagnosticBag b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var result = new DiagnosticBag();
        result.AddRange(a._items);
        result.AddRange(b._items);
        return result;
    }
}
