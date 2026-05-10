namespace Linerule.Core;

/// <summary>
/// Visual configuration of the overlay. v0.1 only exposes the typoscope mask:
/// <see cref="MaskColor"/> RGB, <see cref="Thickness"/> for the slit width,
/// <see cref="Opacity"/> for the dim region's alpha. The earlier
/// <c>BarColor</c> field was dropped (see memory: <c>feedback_linerule_typoscope_only</c>).
/// </summary>
public sealed record OverlayConfig(
    Rgba MaskColor,
    Thickness Thickness,
    Opacity Opacity)
{
    public static OverlayConfig Default { get; } = new(
        MaskColor: Rgba.DefaultMask,
        Thickness: Thickness.Default,
        Opacity: Opacity.Default);
}
