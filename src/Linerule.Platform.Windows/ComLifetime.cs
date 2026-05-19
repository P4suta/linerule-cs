using System.Runtime.InteropServices.Marshalling;

namespace Linerule.Platform.Windows;

/// <summary>
/// Replacement for <c>Marshal.ReleaseComObject</c> / <c>Marshal.FinalReleaseComObject</c>
/// under <c>DisableRuntimeMarshalling=true</c>. The legacy Marshal helpers
/// only support runtime-based COM interop and trigger SYSLIB1099 on every
/// CsWin32-generated <c>[GeneratedComInterface]</c> type. The modern AOT-
/// friendly equivalent is to ask the source-generated wrapper directly:
/// every object returned through a <c>[GeneratedComInterface]</c> out
/// parameter is a <see cref="ComObject"/>, and <see cref="ComObject.FinalRelease"/>
/// decrements the underlying <c>IUnknown</c> refcount in one shot.
/// </summary>
internal static class ComLifetime
{
    public static void Release(object? com)
    {
        if (com is ComObject co)
        {
            co.FinalRelease();
        }
    }
}
