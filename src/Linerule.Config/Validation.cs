namespace Linerule.Config;

/// <summary>
/// Applicative validation carrier: a value of type <typeparamref name="T"/>
/// paired with a <see cref="DiagnosticBag"/> of accumulated diagnostics. The
/// error side is a *monoid* (<see cref="DiagnosticBag.Combine"/>), so several
/// independent checks can be composed via
/// <see cref="Validation.Apply2{TA,TB,TResult}"/> and related combinators
/// *without* short-circuiting — both branches run and every diagnostic
/// surfaces, which is the UX the user expects for config validation (monadic
/// Bind would report only the first failure).
///
/// <para>
/// <b>Ok vs. Fail contract</b>: <see cref="IsOk"/> is true when the bag's
/// running join is below <see cref="DiagnosticSeverity.Error"/>; in that case
/// <see cref="Value"/> holds the parsed value (or fallback supplied by the
/// validator). When <see cref="IsOk"/> is false, <see cref="Value"/> is
/// <c>default(T)</c> and callers must consult the bag.
/// </para>
///
/// <para>
/// The type is unconstrained on <typeparamref name="T"/> so both value-type
/// fields (e.g. <c>Validation&lt;int&gt;</c>) and reference-type sections
/// (e.g. <c>Validation&lt;HudColors&gt;</c>) share the same plumbing.
/// </para>
/// </summary>
public readonly record struct Validation<T>(T Value, DiagnosticBag Errors)
{
    /// <summary><see langword="true"/> iff no Error-severity diagnostic has been accumulated.</summary>
    public bool IsOk => !(Errors?.IsFatal ?? false);
}

/// <summary>
/// Smart constructors and applicative combinators for <see cref="Validation{T}"/>.
/// The <c>ApplyN</c> family is the heart of applicative validation: independent
/// checks run in parallel, their errors concatenate via the
/// <see cref="DiagnosticBag"/> monoid, and the combining function
/// <c>f</c> is always invoked — every <see cref="Validation{T}"/> produced by
/// <see cref="RangeValidator.InRange{T}"/> and the per-section validators
/// already carries a fallback in <see cref="Validation{T}.Value"/>, so the
/// tuple plugs through to <c>f</c> regardless of severity. This "best-effort
/// with diagnostics" shape (vs. strict short-circuit Either) is what lets
/// the config pipeline surface *every* problem in a single error report.
/// </summary>
public static class Validation
{
    public static Validation<T> Ok<T>(T value) => new(value, DiagnosticBag.Empty);

    public static Validation<T> Fail<T>(DiagnosticBag errors) => new(default!, errors ?? DiagnosticBag.Empty);

    /// <summary>
    /// Functor map. Preserves the bag (warnings carry through) while
    /// transforming the value in the Ok case; on Fail the function is not
    /// invoked because <see cref="Fail{T}"/> carries no usable value.
    /// </summary>
    public static Validation<TResult> Map<T, TResult>(this Validation<T> v, Func<T, TResult> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return v.IsOk ? new Validation<TResult>(f(v.Value), v.Errors) : new Validation<TResult>(default!, v.Errors);
    }

    /// <summary>
    /// Applicative product of two independent validations. Both bags are
    /// always combined; the function <paramref name="f"/> is always invoked
    /// on <c>(a.Value, b.Value)</c> because in the validation flow every
    /// branch supplies a usable fallback even when its bag is fatal.
    /// </summary>
    public static Validation<TResult> Apply2<TA, TB, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Func<TA, TB, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = DiagnosticBag.Combine(a.Errors ?? DiagnosticBag.Empty, b.Errors ?? DiagnosticBag.Empty);
        return new Validation<TResult>(f(a.Value, b.Value), combined);
    }

    public static Validation<TResult> Apply3<TA, TB, TC, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Validation<TC> c,
        Func<TA, TB, TC, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors);
        return new Validation<TResult>(f(a.Value, b.Value, c.Value), combined);
    }

    public static Validation<TResult> Apply4<TA, TB, TC, TD, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Validation<TC> c,
        Validation<TD> d,
        Func<TA, TB, TC, TD, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors);
        return new Validation<TResult>(f(a.Value, b.Value, c.Value, d.Value), combined);
    }

    public static Validation<TResult> Apply5<TA, TB, TC, TD, TE, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Validation<TC> c,
        Validation<TD> d,
        Validation<TE> e,
        Func<TA, TB, TC, TD, TE, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors);
        return new Validation<TResult>(f(a.Value, b.Value, c.Value, d.Value, e.Value), combined);
    }

    public static Validation<TResult> Apply6<TA, TB, TC, TD, TE, TF, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Validation<TC> c,
        Validation<TD> d,
        Validation<TE> e,
        Validation<TF> ff,
        Func<TA, TB, TC, TD, TE, TF, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors, ff.Errors);
        return new Validation<TResult>(f(a.Value, b.Value, c.Value, d.Value, e.Value, ff.Value), combined);
    }

    public static Validation<TResult> Apply7<TA, TB, TC, TD, TE, TF, TG, TResult>(
        Validation<TA> a,
        Validation<TB> b,
        Validation<TC> c,
        Validation<TD> d,
        Validation<TE> e,
        Validation<TF> ff,
        Validation<TG> gg,
        Func<TA, TB, TC, TD, TE, TF, TG, TResult> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors, ff.Errors, gg.Errors);
        return new Validation<TResult>(f(a.Value, b.Value, c.Value, d.Value, e.Value, ff.Value, gg.Value), combined);
    }

    private static DiagnosticBag CombineAll(params DiagnosticBag?[] bags)
    {
        var result = DiagnosticBag.Empty;
        foreach (var b in bags)
        {
            result = DiagnosticBag.Combine(result, b ?? DiagnosticBag.Empty);
        }
        return result;
    }
}
