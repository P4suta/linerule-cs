using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// Validating newtype: opacity in <c>1..=255</c>. Default <c>0xAA</c> (~67%).
/// All construction goes through <see cref="TryCreate"/> so the invariant is
/// total at the type boundary.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Opacity
{
    public const byte MinValue = 1;
    public const byte MaxValue = 255;

    public byte Value { get; }

    private Opacity(byte value) => Value = value;

    public static Opacity Default { get; } = new(0xAA);

    /// <summary>
    /// Indicator-stripe default (0x80, ~50% raw). Used by <c>Render.Frame</c>
    /// to seed the mode-indicator glyph alpha before <see cref="ToPerceptualByte"/>
    /// reshapes it for the L* curve.
    /// </summary>
    public static Opacity IndicatorDefault { get; } = new(0x80);

    public static Result<Opacity, CoreError> TryCreate(int value) =>
        value is >= MinValue and <= MaxValue
            ? Result.Ok<Opacity, CoreError>(new Opacity((byte)value))
            : Result.Err<Opacity, CoreError>(new CoreError.Opacity(value));

    /// <summary>Saturating add: <c>BumpOpacity</c> uses this for hotkey-driven adjustment.</summary>
    public Opacity SaturatingAdd(int delta) => new((byte)Math.Clamp((long)Value + delta, MinValue, MaxValue));

    /// <summary>
    /// Map this opacity into the perceptual L* curve and round back to byte.
    /// User-facing controls (hotkey-driven <c>BumpOpacity</c>) move in linear
    /// 1..255 steps; the render layer applies <see cref="PerceptualOpacity.Lstar"/>
    /// so each step lands at a visually-equal position on the screen. The
    /// inverse mapping (perceptual byte → linear domain) is intentionally
    /// not provided — domain code stays linear, only the boundary to D2D
    /// receives the curved value. See ADR-0016.
    /// </summary>
    public byte ToPerceptualByte()
    {
        var linear = Value / 255f;
        var perceptual = PerceptualOpacity.Lstar(linear);
        return (byte)Math.Clamp(MathF.Round(perceptual * 255f, MidpointRounding.AwayFromZero), 0f, 255f);
    }

    public override string ToString() => $"Opacity({Value})";
}
