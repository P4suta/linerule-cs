using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// Validating newtype: thickness in <c>1..=2048</c> logical pixels. Default <c>28</c>.
/// Upper bound has grown twice on user request: 512 → 1024 (long-press
/// auto-repeat landed 2026-05-11), then 1024 → 2048 the same day after
/// "もっと画面全体の一歩手前ぐらいまでどでかく" — i.e. let the slit cover
/// nearly the entire viewport even on a 4K display.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Thickness
{
    public const ushort MinValue = 1;
    public const ushort MaxValue = 2048;

    public ushort Value { get; }

    private Thickness(ushort value) => Value = value;

    public static Thickness Default { get; } = new(28);

    public static Result<Thickness, CoreError> TryCreate(int value) =>
        value is >= MinValue and <= MaxValue
            ? Result.Ok<Thickness, CoreError>(new Thickness((ushort)value))
            : Result.Err<Thickness, CoreError>(new CoreError.Thickness(value));

    public Thickness SaturatingAdd(int delta) => new((ushort)Math.Clamp((long)Value + delta, MinValue, MaxValue));

    public override string ToString() => $"Thickness({Value})";
}
