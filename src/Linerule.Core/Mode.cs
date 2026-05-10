namespace Linerule.Core;

/// <summary>
/// Overlay rendering mode. v0.1 ships only the typoscope (mask) feature; the
/// bar / highlight idea was dropped per user feedback (see memory:
/// <c>feedback_linerule_typoscope_only</c>). Closed payload-less sum.
/// </summary>
/// <remarks>
/// <para>
/// Switch expressions over this enum carry a defensive
/// <c>_ =&gt; throw new UnreachableException()</c> arm to handle the cast-coerced
/// case (e.g. <c>(Mode)42</c>). This is the idiomatic C# valve, not bent
/// architecture. See <c>docs/adr/0002-state-model.md</c>.
/// </para>
/// </remarks>
public enum Mode
{
    /// <summary>Overlay hidden.</summary>
    Off = 0,

    /// <summary>Horizontal typoscope: top + bottom dimmed, slit at cursor Y (横書き reading).</summary>
    Horizontal = 1,

    /// <summary>Vertical typoscope: left + right dimmed, slit at cursor X (縦書き reading).</summary>
    Vertical = 2,
}
