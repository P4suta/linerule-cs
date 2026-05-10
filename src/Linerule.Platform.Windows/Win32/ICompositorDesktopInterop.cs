using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// COM interop for <c>Windows.UI.Composition.Compositor</c> →
/// <see cref="global::Windows.UI.Composition.Desktop.DesktopWindowTarget"/>.
/// The only way to host a Composition visual tree inside a non-XAML Win32
/// <c>HWND</c> without <c>ContentIsland</c> +
/// <c>DesktopAttachedSiteBridge</c>.
///
/// <para>
/// GUID and signature mirror PowerToys MouseHighlighter
/// (<c>src/modules/MouseUtils/MouseHighlighter/MouseHighlighter.cpp</c>) and
/// the public Microsoft Win32 Composition sample
/// (<c>Windows.UI.Composition-Win32-Samples/dotnet/WPF/AcrylicEffect/CompositionHost.cs</c>).
/// QueryInterface from a freshly-constructed <c>Compositor</c> via
/// <c>(ICompositorDesktopInterop)(object)compositor</c>.
/// </para>
///
/// <para>
/// The <c>out IntPtr</c> shape (instead of <c>out DesktopWindowTarget</c>)
/// keeps the interface AOT-friendly via <see cref="GeneratedComInterfaceAttribute"/>;
/// the caller converts to the WinRT projection through
/// <see cref="Marshal.GetObjectForIUnknown(IntPtr)"/> and balances the
/// AddRef with <see cref="Marshal.Release(IntPtr)"/>.
/// </para>
///
/// <para>
/// Microsoft.UI.Composition's <c>Compositor</c> does NOT expose this
/// interface — its attachment story is <c>ContentIsland.Create</c> +
/// <c>DesktopAttachedSiteBridge.Connect</c>, which wedges natively on
/// <c>WS_EX_LAYERED</c> HWNDs as of WinAppSDK 2.0 (verified 2026-05-11).
/// See ADR-0009 v3.
/// </para>
/// </summary>
[GeneratedComInterface]
[Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
internal partial interface ICompositorDesktopInterop
{
    void CreateDesktopWindowTarget(
        IntPtr hwndTarget,
        [MarshalAs(UnmanagedType.Bool)] bool isTopmost,
        out IntPtr target
    );
}
