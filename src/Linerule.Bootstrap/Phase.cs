using Linerule.Core;

namespace Linerule.Bootstrap;

/// <summary>
/// A boot phase: a Kleisli arrow <c>TIn ↝ TOut</c> in the
/// <c>Result&lt;_, BootstrapError&gt;</c> error monad. Composition (via
/// <see cref="PhaseExtensions.Then"/>) is associative; the identity arrow
/// is <see cref="Phase.Identity"/>. Wrong-order composition becomes a
/// CS1503 compile error because each phase's <c>TIn</c> / <c>TOut</c>
/// carries a capability token (e.g. <c>SqliteOpened</c>, <c>LoggerLive</c>)
/// that the next phase consumes by type.
/// </summary>
public sealed record Phase<TIn, TOut>(
    string Name,
    Func<TIn, CancellationToken, ValueTask<Result<TOut, BootstrapError>>> Run
);

/// <summary>
/// Combinators over <see cref="Phase{TIn, TOut}"/>.
/// </summary>
public static class Phase
{
    /// <summary>Lift a pure synchronous function into a phase.</summary>
    public static Phase<TIn, TOut> Pure<TIn, TOut>(string name, Func<TIn, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new Phase<TIn, TOut>(
            name,
            (input, _) => ValueTask.FromResult(Result.Ok<TOut, BootstrapError>(f(input)))
        );
    }

    /// <summary>
    /// Identity arrow: passes the input through unchanged. Useful as the
    /// neutral element when composing phases conditionally (e.g. "if the
    /// config requests --no-crash-dump, use Identity here").
    /// </summary>
    public static Phase<T, T> Identity<T>() => Pure<T, T>("identity", x => x);

    /// <summary>
    /// Lift a synchronous <c>Result</c>-returning function into a phase.
    /// </summary>
    public static Phase<TIn, TOut> FromResult<TIn, TOut>(string name, Func<TIn, Result<TOut, BootstrapError>> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new Phase<TIn, TOut>(name, (input, _) => ValueTask.FromResult(f(input)));
    }

    /// <summary>
    /// Lift an async <c>Result</c>-returning function into a phase. Named
    /// <c>FromArrow</c> rather than <c>FromAsync</c> to satisfy RCS1047
    /// (the factory itself returns a <see cref="Phase{TIn, TOut}"/> record
    /// synchronously — the inner delegate is what's async).
    /// </summary>
    public static Phase<TIn, TOut> FromArrow<TIn, TOut>(
        string name,
        Func<TIn, CancellationToken, ValueTask<Result<TOut, BootstrapError>>> f
    )
    {
        ArgumentNullException.ThrowIfNull(f);
        return new Phase<TIn, TOut>(name, f);
    }
}
