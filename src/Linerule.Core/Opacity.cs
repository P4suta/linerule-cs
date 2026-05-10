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

    public static Result<Opacity, CoreError> TryCreate(int value) =>
        value is >= MinValue and <= MaxValue
            ? Result.Ok<Opacity, CoreError>(new Opacity((byte)value))
            : Result.Err<Opacity, CoreError>(new CoreError.Opacity(value));

    /// <summary>Saturating add: <c>BumpOpacity</c> uses this for hotkey-driven adjustment.</summary>
    public Opacity SaturatingAdd(int delta) =>
        new((byte)Math.Clamp((long)Value + delta, MinValue, MaxValue));

    public override string ToString() => $"Opacity({Value})";
}
