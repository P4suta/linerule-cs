namespace Linerule.Core;

/// <summary>
/// Render geometry. Open for extension via additional sealed records (path, glyph,
/// rounded rect) but closed to external derivation. Nested closed-DU pattern; see
/// <c>docs/adr/0002-state-model.md</c> for the analyzer-relaxation rationale.
/// </summary>
public abstract record Geometry
{
    private protected Geometry() { }

    /// <summary>Axis-aligned half-open rectangle in logical-pixel space.</summary>
    public sealed record Rect(ScreenRect<Logical> Bounds) : Geometry;
}
