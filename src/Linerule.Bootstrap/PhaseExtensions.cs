using Linerule.Core;

namespace Linerule.Bootstrap;

/// <summary>
/// Kleisli composition and the unit law over <see cref="Phase{TIn, TOut}"/>.
/// </summary>
public static class PhaseExtensions
{
    /// <summary>
    /// Sequential composition <c>f &gt;=&gt; g</c>: run <paramref name="f"/>;
    /// on Ok, feed its output to <paramref name="g"/>; on Err, short-circuit
    /// without touching <paramref name="g"/>. Associative.
    /// </summary>
    public static Phase<TIn, TOut> Then<TIn, TMid, TOut>(this Phase<TIn, TMid> f, Phase<TMid, TOut> g)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(g);
        return new Phase<TIn, TOut>(
            $"{f.Name}>=>{g.Name}",
            async (input, ct) =>
            {
                try
                {
                    var firstResult = await f.Run(input, ct).ConfigureAwait(false);
                    if (firstResult is Result<TMid, BootstrapError>.Err firstErr)
                    {
                        return Result.Err<TOut, BootstrapError>(firstErr.Error);
                    }
                    var firstOk = (Result<TMid, BootstrapError>.Ok)firstResult;
                    return ct.IsCancellationRequested
                        ? Result.Err<TOut, BootstrapError>(new BootstrapError.Cancelled(g.Name))
                        : await g.Run(firstOk.Value, ct).ConfigureAwait(false);
                }
                catch (System.Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    return Result.Err<TOut, BootstrapError>(new BootstrapError.Threw(g.Name, ex));
                }
            }
        );
    }

    /// <summary>
    /// Map the <c>TOut</c> of an existing phase through a pure function.
    /// </summary>
    public static Phase<TIn, TNext> Map<TIn, TOut, TNext>(this Phase<TIn, TOut> phase, Func<TOut, TNext> f)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(f);
        return phase.Then(Phase.Pure<TOut, TNext>($"{phase.Name}.map", f));
    }
}
