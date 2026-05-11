namespace Linerule.Core;

/// <summary>Visual configuration of the typoscope: mask color, slit width, and dim region alpha.</summary>
public sealed record OverlayConfig(Rgba MaskColor, Thickness Thickness, Opacity Opacity)
{
    public static OverlayConfig Default { get; } =
        new(MaskColor: Rgba.DefaultMask, Thickness: Thickness.Default, Opacity: Opacity.Default);
}
