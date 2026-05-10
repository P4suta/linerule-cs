using System.Runtime.InteropServices;

namespace Linerule.Core;

/// <summary>32-bit RGBA color. Components are bytes; equality is structural.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    /// <summary>
    /// Default for the typoscope mask: <b>pure black</b> at ~80% alpha. WinAppSDK +
    /// DirectComposition gives true per-pixel alpha so no color-key workaround is
    /// needed (Rust v0.1 had to use <c>(8, 8, 8)</c> near-black). The alpha
    /// component is overridden at render time by <see cref="Opacity"/>.
    /// </summary>
    public static Rgba DefaultMask { get; } = new(0x00, 0x00, 0x00, 0xCC);

    /// <summary>Replace the alpha component, keeping RGB intact.</summary>
    public Rgba WithAlpha(byte alpha) => this with { A = alpha };
}
