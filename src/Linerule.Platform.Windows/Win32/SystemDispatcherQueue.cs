using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Win32;

/// <summary>
/// Ensures the current thread has a <see cref="Windows.System.DispatcherQueue"/>
/// (OS-level), which <see cref="Windows.UI.Composition.Compositor"/>'s
/// constructor requires.
/// <para>
/// <c>Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread()</c>
/// (WinAppSDK 2.0) does <em>not</em> establish the OS-level queue — the two
/// dispatcher worlds are independent. The OS-level <c>WinRT</c> projection only
/// exposes <c>CreateOnDedicatedThread</c>; "current thread" creation goes
/// through the unmanaged <c>CreateDispatcherQueueController</c> export in
/// <c>CoreMessaging.dll</c>. We hold the controller as a static GC root so it
/// persists for the process lifetime.
/// </para>
/// </summary>
internal static class SystemDispatcherQueue
{
    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_STA = 2;

    private static IntPtr _controllerHandle;

    /// <summary>True once <see cref="EnsureForCurrentThread"/> has installed the OS dispatcher queue.</summary>
    public static bool IsInitialized => _controllerHandle != IntPtr.Zero;

    public static void EnsureForCurrentThread()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = DQTYPE_THREAD_CURRENT,
            apartmentType = DQTAT_COM_STA,
        };

        var hr = NativeBridge.CreateDispatcherQueueController(options, out var handle);
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
        _controllerHandle = handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    private static class NativeBridge
    {
        [DllImport("CoreMessaging.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CreateDispatcherQueueController(
            DispatcherQueueOptions options,
            out IntPtr dispatcherQueueController);
    }
}
