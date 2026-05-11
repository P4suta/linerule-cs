namespace Linerule.Config;

/// <summary>
/// HUD inner padding in logical pixels (multiplied by DPI scale at render time).
/// Three levels: outer <see cref="Edge"/>, between <see cref="Section"/>s
/// (groups of rows), and between adjacent <see cref="Row"/>s.
/// </summary>
public sealed record HudPadding(float Edge, float Section, float Row)
{
    public static HudPadding Default { get; } = new(Edge: 24f, Section: 16f, Row: 8f);
}
