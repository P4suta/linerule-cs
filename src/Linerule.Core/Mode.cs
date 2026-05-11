namespace Linerule.Core;

/// <summary>
/// Overlay rendering mode. Closed payload-less sum.
/// Switch expressions carry <c>_ =&gt; throw new UnreachableException()</c>
/// for cast-coerced values; see ADR-0002.
/// </summary>
public enum Mode
{
    Off = 0,
    Horizontal = 1,
    Vertical = 2,
}
