namespace Linerule.Config;

/// <summary>
/// Applicative validation carrier: a value of type <typeparamref name="T"/>
/// paired with a <see cref="DiagnosticBag"/> of accumulated diagnostics. The
/// error side is a *monoid* (<see cref="DiagnosticBag.Combine"/>), so several
/// independent checks can be composed via
/// <see cref="Validation.Apply2{A,B,C}"/> and related combinators *without*
/// short-circuiting — both branches run and every diagnostic surfaces, which
/// is the UX the user expects for config validation (monadic Bind would
/// report only the first failure).
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
    public static Validation<U> Map<T, U>(this Validation<T> v, Func<T, U> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return v.IsOk ? new Validation<U>(f(v.Value), v.Errors) : new Validation<U>(default!, v.Errors);
    }

    /// <summary>
    /// Applicative product of two independent validations. Both bags are
    /// always combined; the function <paramref name="f"/> is always invoked
    /// on <c>(a.Value, b.Value)</c> because in the validation flow every
    /// branch supplies a usable fallback even when its bag is fatal.
    /// </summary>
    public static Validation<C> Apply2<A, B, C>(Validation<A> a, Validation<B> b, Func<A, B, C> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = DiagnosticBag.Combine(a.Errors ?? DiagnosticBag.Empty, b.Errors ?? DiagnosticBag.Empty);
        return new Validation<C>(f(a.Value, b.Value), combined);
    }

    public static Validation<D> Apply3<A, B, C, D>(
        Validation<A> a,
        Validation<B> b,
        Validation<C> c,
        Func<A, B, C, D> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors);
        return new Validation<D>(f(a.Value, b.Value, c.Value), combined);
    }

    public static Validation<E> Apply4<A, B, C, D, E>(
        Validation<A> a,
        Validation<B> b,
        Validation<C> c,
        Validation<D> d,
        Func<A, B, C, D, E> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors);
        return new Validation<E>(f(a.Value, b.Value, c.Value, d.Value), combined);
    }

    public static Validation<F> Apply5<A, B, C, D, E, F>(
        Validation<A> a,
        Validation<B> b,
        Validation<C> c,
        Validation<D> d,
        Validation<E> e,
        Func<A, B, C, D, E, F> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors);
        return new Validation<F>(f(a.Value, b.Value, c.Value, d.Value, e.Value), combined);
    }

    public static Validation<G> Apply6<A, B, C, D, E, F, G>(
        Validation<A> a,
        Validation<B> b,
        Validation<C> c,
        Validation<D> d,
        Validation<E> e,
        Validation<F> ff,
        Func<A, B, C, D, E, F, G> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors, ff.Errors);
        return new Validation<G>(f(a.Value, b.Value, c.Value, d.Value, e.Value, ff.Value), combined);
    }

    public static Validation<H> Apply7<A, B, C, D, E, F, G, H>(
        Validation<A> a,
        Validation<B> b,
        Validation<C> c,
        Validation<D> d,
        Validation<E> e,
        Validation<F> ff,
        Validation<G> gg,
        Func<A, B, C, D, E, F, G, H> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        var combined = CombineAll(a.Errors, b.Errors, c.Errors, d.Errors, e.Errors, ff.Errors, gg.Errors);
        return new Validation<H>(f(a.Value, b.Value, c.Value, d.Value, e.Value, ff.Value, gg.Value), combined);
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
