using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// 2-element ABI shape for the <c>Windows.Graphics.SizeInt32</c> WinRT
/// projection, declared locally so it is unambiguously blittable for
/// <see cref="GeneratedComInterfaceAttribute"/>-generated marshaling.
/// The WinRT projection types carry runtime marshaling metadata that the
/// source-gen marshaller refuses to handle (SYSLIB1051); a plain
/// <see cref="StructLayoutAttribute"/> sequential struct is the canonical
/// workaround.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinGraphicsSizeInt32
{
    public int Width;
    public int Height;
}
