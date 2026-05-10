namespace Linerule.Core;

/// <summary>
/// Closed sum type: <c>Ok(value) | Err(error)</c>. C# dual of Rust's <c>Result&lt;T, E&gt;</c>.
/// External derivation is closed by the <see langword="private protected"/> ctor.
/// Nested closed-DU pattern; see <c>docs/adr/0002-state-model.md</c>.
/// <para>
/// Switch expressions carry a defensive <c>_ =&gt; throw</c> arm because the C#
/// compiler's exhaustiveness analysis is conservative even for sealed hierarchies
/// (CS8509).
/// </para>
/// </summary>
public abstract record Result<T, TError>
{
    private protected Result() { }

    public sealed record Ok(T Value) : Result<T, TError>;
    public sealed record Err(TError Error) : Result<T, TError>;

    public bool IsOk => this is Ok;
    public bool IsErr => this is Err;

    public TOut Match<TOut>(Func<T, TOut> ok, Func<TError, TOut> err)
    {
        ArgumentNullException.ThrowIfNull(ok);
        ArgumentNullException.ThrowIfNull(err);
        return this switch
        {
            Ok o => ok(o.Value),
            Err e => err(e.Error),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    public Result<TOut, TError> Map<TOut>(Func<T, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return this switch
        {
            Ok o => new Result<TOut, TError>.Ok(f(o.Value)),
            Err e => new Result<TOut, TError>.Err(e.Error),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    public Result<TOut, TError> Bind<TOut>(Func<T, Result<TOut, TError>> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return this switch
        {
            Ok o => f(o.Value),
            Err e => new Result<TOut, TError>.Err(e.Error),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }

    public Result<T, TErrorOut> MapErr<TErrorOut>(Func<TError, TErrorOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return this switch
        {
            Ok o => new Result<T, TErrorOut>.Ok(o.Value),
            Err e => new Result<T, TErrorOut>.Err(f(e.Error)),
            _ => throw new System.Diagnostics.UnreachableException(),
        };
    }
}

public static class Result
{
    public static Result<T, TError> Ok<T, TError>(T value) => new Result<T, TError>.Ok(value);

    public static Result<T, TError> Err<T, TError>(TError error) => new Result<T, TError>.Err(error);
}
