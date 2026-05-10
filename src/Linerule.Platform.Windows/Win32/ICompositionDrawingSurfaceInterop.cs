using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// COM interop for <c>Windows.UI.Composition.CompositionDrawingSurface</c>.
/// The canonical bridge from Composition's pixel-allocation discipline
/// into native 2D rendering: <see cref="BeginDraw"/> hands back an
/// <c>ID2D1DeviceContext</c> (typed by <paramref name="iid"/>) plus the
/// pixel offset where this update lands inside the surface; the caller
/// paints with Direct2D + DirectWrite; <see cref="EndDraw"/> blits the
/// result back into the composition tree.
///
/// <para>
/// All parameters are blittable so the interface compiles under
/// <see cref="GeneratedComInterfaceAttribute"/>. The returned
/// <see cref="IntPtr"/> for the drawing object is owned by the surface —
/// the caller wraps it with <see cref="Marshal.GetObjectForIUnknown(IntPtr)"/>
/// for the duration of the draw and releases via
/// <see cref="Marshal.ReleaseComObject(object)"/> + the surface's own
/// <see cref="EndDraw"/>.
/// </para>
///
/// <para>
/// GUID and signatures per the Microsoft Win32 Composition sample
/// (<c>Windows.UI.Composition-Win32-Samples</c>). The <paramref name="iid"/>
/// for Direct2D 1.1 device context is
/// <c>{E8F7FE7A-191C-466D-AD95-975678BDA998}</c>.
/// </para>
/// </summary>
[GeneratedComInterface]
[Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE")]
internal partial interface ICompositionDrawingSurfaceInterop
{
    void BeginDraw(IntPtr updateRect, in Guid iid, out IntPtr updateObject, out WinGraphicsPointInt32 updateOffset);
    void EndDraw();
    void Resize(WinGraphicsSizeInt32 sizePixels);
    void Scroll(IntPtr scrollRect, IntPtr clipRect, int offsetX, int offsetY);
    void ResumeDraw();
    void SuspendDraw();
}
