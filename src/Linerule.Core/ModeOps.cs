namespace Linerule.Core;

/// <summary>Extension surface for <see cref="Mode"/>.</summary>
public static class ModeOps
{
    /// <summary>3-cycle: <c>Off → Horizontal → Vertical → Off</c>. Property-tested as period 3.</summary>
    public static Mode Cycle(this Mode mode) => mode switch
    {
        Mode.Off => Mode.Horizontal,
        Mode.Horizontal => Mode.Vertical,
        Mode.Vertical => Mode.Off,
        _ => throw new System.Diagnostics.UnreachableException($"invalid Mode: {(int)mode}"),
    };
}
