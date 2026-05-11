using System.Runtime.InteropServices;
using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Pure HUD-opacity fade kernel: <c>(state, cursor, hudBounds, fadeDecayPx) → opacity ∈ [0, 1]</c>.
/// Fade formula: <c>opacity = 1 − exp(−d/τ)</c> where <c>d</c> is the gap
/// between the reading-ruler slit and the HUD rectangle and <c>τ = fadeDecayPx</c>.
/// </summary>
public static class HudFadeKernel
{
    /// <summary>HUD bounding box in logical pixels.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct HudBounds(float Left, float Top, float Right, float Bottom)
    {
        public float Width => Right - Left;
        public float Height => Bottom - Top;
    }

    /// <summary>
    /// Target HUD opacity for the current state. Returns 1 when the slit is
    /// hidden or off (HUD fully visible), 0 on exact overlap, smooth
    /// exponential decay in between.
    /// </summary>
    public static float ComputeOpacity(State state, Point<Logical> cursor, HudBounds hud, float fadeDecayPx)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (fadeDecayPx <= 0f)
        {
            return 1f; // Division by zero guard; degenerate input → fully visible.
        }
        var distance = SlitToHudGap(state, cursor, hud);
        return 1f - MathF.Exp(-distance / fadeDecayPx);
    }

    /// <summary>
    /// Logical-pixel gap between the cursor's "slit rectangle" and the HUD
    /// rectangle.
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="Mode.Horizontal"/> / <see cref="Mode.Vertical"/> — the
    ///     slit is an infinite line of width <c>thickness</c> along the
    ///     orthogonal axis; the gap is the 1-D <see cref="AxisGap"/> along
    ///     that axis.
    ///   </item>
    ///   <item>
    ///     <see cref="Mode.Off"/> — the cursor is treated as a
    ///     <c>thickness × thickness</c> point-rect so the HUD fades on cursor
    ///     proximity. Gap is the 2-D Euclidean distance <c>√(dx² + dy²)</c>
    ///     between that point-rect and the HUD bounds.
    ///   </item>
    /// </list>
    ///
    /// Returns <see cref="float.PositiveInfinity"/> only when the overlay is
    /// fully hidden (<c>state.Visible == false</c>); in that case the HUD
    /// stays fully opaque because no slit / point-rect is engaged.
    /// </summary>
    public static float SlitToHudGap(State state, Point<Logical> cursor, HudBounds hud)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Visible)
        {
            return float.PositiveInfinity;
        }
        var thickness = state.Config.Thickness.Value;
        var halfT = thickness / 2f;
        return state.Mode switch
        {
            Mode.Horizontal => AxisGap(cursor.Y - halfT, cursor.Y + halfT, hud.Top, hud.Bottom),
            Mode.Vertical => AxisGap(cursor.X - halfT, cursor.X + halfT, hud.Left, hud.Right),
            Mode.Off => PointRectDistance(cursor, halfT, hud),
            _ => throw new System.Diagnostics.UnreachableException("unknown overlay mode"),
        };
    }

    /// <summary>
    /// 2-D Euclidean distance between an axis-aligned cursor "point rect"
    /// (a square of half-width <paramref name="half"/> centered on
    /// <paramref name="cursor"/>) and the HUD rectangle. Returns 0 when the
    /// rects overlap, otherwise the straight-line gap. Used by
    /// <see cref="Mode.Off"/> so HUD fade behaves consistently across modes.
    /// </summary>
    private static float PointRectDistance(Point<Logical> cursor, float half, HudBounds hud)
    {
        var dx = AxisGap(cursor.X - half, cursor.X + half, hud.Left, hud.Right);
        var dy = AxisGap(cursor.Y - half, cursor.Y + half, hud.Top, hud.Bottom);
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Non-negative gap between two 1-D intervals on the same axis;
    /// 0 when they overlap. Branch-free form of
    /// <c>if (a ends before b starts) gap; else if (b ends before a) gap; else 0</c>.
    /// </summary>
    public static float AxisGap(float aLo, float aHi, float bLo, float bHi) =>
        Math.Max(0f, Math.Max(bLo - aHi, aLo - bHi));
}
