using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// Validating newtype: thickness in <c>1..=512</c> logical pixels. Default <c>28</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Thickness
{
    public const ushort MinValue = 1;
    public const ushort MaxValue = 512;

    public ushort Value { get; }

    private Thickness(ushort value) => Value = value;

    public static Thickness Default { get; } = new(28);

    public static Result<Thickness, CoreError> TryCreate(int value) =>
        value is >= MinValue and <= MaxValue
            ? Result.Ok<Thickness, CoreError>(new Thickness((ushort)value))
            : Result.Err<Thickness, CoreError>(new CoreError.Thickness(value));

    public Thickness SaturatingAdd(int delta) =>
        new((ushort)Math.Clamp((long)Value + delta, MinValue, MaxValue));

    public override string ToString() => $"Thickness({Value})";
}
