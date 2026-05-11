namespace Linerule.Bootstrap;

/// <summary>
/// Initial input to <see cref="Linerule.Bootstrap.Phase{TIn, TOut}"/> compositions: the command-line argv
/// captured by the process entry point. Wrap raw <c>string[]</c> in a typed
/// record so the first phase has something to pattern-match on.
/// </summary>
public sealed record BootArgs(IReadOnlyList<string> Argv)
{
    public static BootArgs FromArgv(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new BootArgs(args);
    }
}
