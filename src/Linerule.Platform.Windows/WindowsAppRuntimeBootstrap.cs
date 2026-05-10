using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Linerule.Platform.Windows;

/// <summary>
/// Initializes / tears down the Windows App SDK runtime for processes that don't
/// use the framework-package singleton bootstrap (i.e. unpackaged, no XAML
/// <c>Application</c>). Must run before any <c>Microsoft.UI.*</c> API call.
/// See <c>docs/adr/0001-tech-stack.md</c> §"WindowsAppRuntime bootstrap".
/// </summary>
public sealed class WindowsAppRuntimeBootstrap : IDisposable
{
    private bool _disposed;

    private WindowsAppRuntimeBootstrap() { }

    /// <summary>
    /// Initialize using the WindowsAppSDK 2.x major-version channel.
    /// Idempotent at the SDK level but this wrapper enforces single-init per process.
    /// </summary>
    public static WindowsAppRuntimeBootstrap Initialize()
    {
        Bootstrap.Initialize(majorMinorVersion: 0x00020000);
        return new WindowsAppRuntimeBootstrap();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Bootstrap.Shutdown();
        _disposed = true;
    }
}
