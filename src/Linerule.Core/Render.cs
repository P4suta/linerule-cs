using System.Collections.Immutable;

namespace Linerule.Core;

/// <summary>
/// Pure render function. Maps the logical state at a moment to a frame. No IO.
/// Property-tested for monitor-bounds clipping and per-mode layer count.
/// v0.1 produces 0 (Off) or 3 layers when active: 2 dim regions + 1 mode indicator.
/// </summary>
public static class Render
{
    /// <summary>Px from the monitor edge to the indicator glyph.</summary>
    private const int IndicatorMargin = 12;

    /// <summary>Long side of the indicator glyph (the bar's length).</summary>
    private const int IndicatorLong = 18;

    /// <summary>Short side of the indicator glyph (the bar's thickness).</summary>
    private const int IndicatorShort = 4;

    public static OverlayFrame Frame(
        Mode mode,
        Point<Logical> cursor,
        ScreenRect<Logical> monitor,
        OverlayConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return mode switch
        {
            Mode.Off => OverlayFrame.Empty,
            Mode.Horizontal => RenderHorizontal(cursor, monitor, config),
            Mode.Vertical => RenderVertical(cursor, monitor, config),
            _ => throw new System.Diagnostics.UnreachableException($"invalid Mode: {(int)mode}"),
        };
    }

    private static OverlayFrame RenderHorizontal(
        Point<Logical> cursor,
        ScreenRect<Logical> monitor,
        OverlayConfig config)
    {
        var thickness = config.Thickness.Value;
        var halfH = thickness / 2;
        var slitTop = ClampToMonitor(cursor.Y - halfH, monitor.Top, monitor.Bottom - thickness);
        var slitBottom = slitTop + thickness;

        var topDim = new ScreenRect<Logical>(
            new Point<Logical>(monitor.Left, monitor.Top),
            monitor.Width,
            (uint)Math.Max(0, slitTop - monitor.Top));
        var bottomDim = new ScreenRect<Logical>(
            new Point<Logical>(monitor.Left, slitBottom),
            monitor.Width,
            (uint)Math.Max(0, monitor.Bottom - slitBottom));

        var maskFill = config.MaskColor.WithAlpha(config.Opacity.Value);
        return new OverlayFrame(ImmutableArray.Create(
            Layer.SolidRect(topDim, maskFill),
            Layer.SolidRect(bottomDim, maskFill),
            BuildIndicator(Mode.Horizontal, monitor)));
    }

    private static OverlayFrame RenderVertical(
        Point<Logical> cursor,
        ScreenRect<Logical> monitor,
        OverlayConfig config)
    {
        var thickness = config.Thickness.Value;
        var halfW = thickness / 2;
        var slitLeft = ClampToMonitor(cursor.X - halfW, monitor.Left, monitor.Right - thickness);
        var slitRight = slitLeft + thickness;

        var leftDim = new ScreenRect<Logical>(
            new Point<Logical>(monitor.Left, monitor.Top),
            (uint)Math.Max(0, slitLeft - monitor.Left),
            monitor.Height);
        var rightDim = new ScreenRect<Logical>(
            new Point<Logical>(slitRight, monitor.Top),
            (uint)Math.Max(0, monitor.Right - slitRight),
            monitor.Height);

        var maskFill = config.MaskColor.WithAlpha(config.Opacity.Value);
        return new OverlayFrame(ImmutableArray.Create(
            Layer.SolidRect(leftDim, maskFill),
            Layer.SolidRect(rightDim, maskFill),
            BuildIndicator(Mode.Vertical, monitor)));
    }

    /// <summary>
    /// Top-right corner mode indicator. Soft white at 50% alpha so it reads on
    /// both the dimmed region and any desktop content under the slit.
    /// Horizontal mode = horizontal bar; Vertical mode = vertical bar — shape
    /// echoes the slit orientation for at-a-glance recognition.
    /// </summary>
    private static Layer BuildIndicator(Mode mode, ScreenRect<Logical> monitor)
    {
        var color = new Rgba(0xFF, 0xFF, 0xFF, 0x80);
        var rect = mode switch
        {
            Mode.Horizontal => new ScreenRect<Logical>(
                new Point<Logical>(monitor.Right - IndicatorMargin - IndicatorLong, monitor.Top + IndicatorMargin),
                (uint)IndicatorLong,
                (uint)IndicatorShort),
            Mode.Vertical => new ScreenRect<Logical>(
                new Point<Logical>(monitor.Right - IndicatorMargin - IndicatorShort, monitor.Top + IndicatorMargin),
                (uint)IndicatorShort,
                (uint)IndicatorLong),
            _ => throw new System.Diagnostics.UnreachableException("indicator only defined for active modes"),
        };
        return Layer.SolidRect(rect, color);
    }

    // For pathological inputs (monitor smaller than thickness) lo > hi can occur
    // — guard before delegating to Math.Clamp, which would throw.
    private static int ClampToMonitor(int v, int lo, int hi) =>
        lo > hi ? lo : Math.Clamp(v, lo, hi);
}
