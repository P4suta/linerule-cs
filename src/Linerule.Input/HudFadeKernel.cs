using System.Runtime.InteropServices;
using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Pure HUD-opacity fade kernel: a function from
/// <c>(state, cursor, hudBounds, fadeDecayPx)</c> to the target HUD opacity in
/// <c>[0, 1]</c>. Extracted from <c>WindowsApp.TickLoop</c> so the geometry +
/// exponential decay is independent of WinAppSDK / DispatcherQueue — every
/// edge case (Off mode, hidden overlay, exact overlap, full separation) is
/// directly property-testable from <c>Linerule.Input.Tests</c>.
///
/// <para>
/// The fade is <c>opacity = 1 − exp(−d/τ)</c> where <c>d</c> is the
/// axis-aligned gap between the reading-ruler slit and the HUD rectangle and
/// <c>τ</c> = <c>fadeDecayPx</c>. Smooth in both directions, no flicker
/// (user request 2026-05-11 「無段階」).
/// </para>
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
            // Degenerate decay length — fall back to the "always visible"
            // limit rather than producing NaN/∞ via division by zero.
            return 1f;
        }
        var distance = SlitToHudGap(state, cursor, hud);
        return 1f - MathF.Exp(-distance / fadeDecayPx);
    }

    /// <summary>
    /// Logical-pixel gap between the slit (axis-aligned around
    /// <paramref name="cursor"/>) and the HUD rectangle along the slit's
    /// dominant axis. Returns <see cref="float.PositiveInfinity"/> when no
    /// slit is currently drawn (Off mode or hidden).
    /// </summary>
    public static float SlitToHudGap(State state, Point<Logical> cursor, HudBounds hud)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Visible)
        {
            return float.PositiveInfinity;
        }
        var thickness = state.Config.Thickness.Value;
        return state.Mode switch
        {
            Mode.Horizontal => AxisGap(cursor.Y - (thickness / 2f), cursor.Y + (thickness / 2f), hud.Top, hud.Bottom),
            Mode.Vertical => AxisGap(cursor.X - (thickness / 2f), cursor.X + (thickness / 2f), hud.Left, hud.Right),
            Mode.Off => float.PositiveInfinity,
            _ => throw new System.Diagnostics.UnreachableException("unknown overlay mode"),
        };
    }

    /// <summary>
    /// Non-negative gap between two 1-D intervals on the same axis;
    /// 0 when they overlap. Branch-free form of
    /// <c>if (a ends before b starts) gap; else if (b ends before a) gap; else 0</c>.
    /// </summary>
    public static float AxisGap(float aLo, float aHi, float bLo, float bHi) =>
        Math.Max(0f, Math.Max(bLo - aHi, aLo - bHi));
}
