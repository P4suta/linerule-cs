using Linerule.Core;

namespace Linerule.Config;

/// <summary>
/// WCAG-style sRGB contrast utilities. Extracted from <c>Validator</c> so the
/// cross-field invariants in <see cref="CrossFieldValidator"/> stay focused on
/// shaping diagnostics — the pure math has no need to touch the bag.
/// </summary>
internal static class ColorContrast
{
    /// <summary>Whether two <see cref="Rgba"/> values agree on the RGB triple (alpha ignored).</summary>
    public static bool RgbEqual(Rgba a, Rgba b) => a.R == b.R && a.G == b.G && a.B == b.B;

    /// <summary>WCAG simple contrast ratio. Result is in <c>[1.0, 21.0]</c> for sRGB inputs.</summary>
    public static double ContrastRatio(Rgba fg, Rgba bg)
    {
        var lf = RelativeLuminance(fg);
        var lb = RelativeLuminance(bg);
        var (light, dark) = lf >= lb ? (lf, lb) : (lb, lf);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double RelativeLuminance(Rgba c)
    {
        var r = Linearize(c.R / 255.0);
        var g = Linearize(c.G / 255.0);
        var b = Linearize(c.B / 255.0);
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Linearize(double srgb) =>
        srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
}
