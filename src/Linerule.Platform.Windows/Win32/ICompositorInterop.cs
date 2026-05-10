using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// COM interop for <c>Windows.UI.Composition.Compositor</c> → composition
/// resources backed by Direct2D/DXGI handles. We use the
/// <see cref="CreateGraphicsDevice"/> entry point to wrap an
/// <c>ID2D1Device</c> in a Composition <c>CompositionGraphicsDevice</c>,
/// which then mints <c>CompositionDrawingSurface</c>s that the HUD renderer
/// paints with Direct2D + DirectWrite via <see cref="ICompositionDrawingSurfaceInterop"/>.
///
/// <para>
/// Inputs/outputs are <see cref="IntPtr"/> raw COM pointers so the
/// interface compiles under <see cref="GeneratedComInterfaceAttribute"/>;
/// callers convert to managed wrappers via
/// <see cref="Marshal.GetIUnknownForObject(object)"/> /
/// <see cref="Marshal.GetObjectForIUnknown(IntPtr)"/> and balance refcounts
/// with <see cref="Marshal.Release(IntPtr)"/>.
/// </para>
///
/// <para>
/// GUID per PowerToys + <c>Windows.UI.Composition-Win32-Samples</c>.
/// See ADR-0009 v3.
/// </para>
/// </summary>
[GeneratedComInterface]
[Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330")]
internal partial interface ICompositorInterop
{
    void CreateCompositionSurfaceForHandle(IntPtr swapChain, out IntPtr result);
    void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr result);
    void CreateGraphicsDevice(IntPtr renderingDevice, out IntPtr result);
}
