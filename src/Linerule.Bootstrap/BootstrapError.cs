namespace Linerule.Bootstrap;

/// <summary>
/// Errors a <see cref="Phase{TIn, TOut}"/> can surface. Closed sum: each
/// variant carries a focused payload (the phase name and the underlying
/// cause as a typed inner). Consumers lift this to
/// <c>LineruleError.BootFault</c> at the CLI boundary.
/// </summary>
public abstract record BootstrapError
{
    private protected BootstrapError() { }

    /// <summary>The phase's <c>Run</c> delegate threw before producing a Result.</summary>
    public sealed record Threw(string PhaseName, System.Exception Cause) : BootstrapError;

    /// <summary>The phase produced an Err that bubbles up unchanged.</summary>
    public sealed record PhaseFailed(string PhaseName, string Reason) : BootstrapError;

    /// <summary>The composition was cancelled before reaching the next phase.</summary>
    public sealed record Cancelled(string PhaseName) : BootstrapError;
}
