namespace Linerule.Bootstrap;

/// <summary>
/// Capability tokens. Each token is the output of one boot phase and the
/// required input of subsequent phases that depend on its presence; the C#
/// type system enforces "you cannot call CrashGuardArmed.Phase() without a
/// LoggerLive in hand" as a CS1503 compile error.
///
/// <para>
/// Tokens are intentionally thin wrappers around the live resource handle
/// (sink, logger root, …). They are <c>IAsyncDisposable</c> where ownership
/// belongs to the boot DAG; the final <c>AppContext</c> drives LIFO teardown.
/// </para>
/// </summary>
public abstract record CapabilityToken
{
    private protected CapabilityToken() { }
}
