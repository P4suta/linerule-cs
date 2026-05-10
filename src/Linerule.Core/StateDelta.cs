using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>
/// Diff produced by <see cref="Reduce"/>. Each component is non-null exactly when
/// the corresponding field of <see cref="State"/> changed; <see cref="ConfigChanged"/>
/// is the coarse "any sub-field of <see cref="OverlayConfig"/> changed" signal.
/// Mirrors Rust's <c>StateDelta</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct StateDelta(Mode? Mode, bool? Visible, bool ConfigChanged)
{
    public static StateDelta None => default;

    public bool IsAny => Mode is not null || Visible is not null || ConfigChanged;
}
