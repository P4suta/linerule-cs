using System;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;

namespace Linerule.Platform.Windows;

/// <summary>
/// Shared <see cref="ID3D11Device"/> factory. Both the dcomp surface chain
/// (overlay) and the D2D device (HUD) consume the same device, created once
/// in <see cref="WindowsApp"/> and threaded through. Factoring keeps the
/// device-creation logic in one place — the BGRA_SUPPORT flag is required
/// for D2D interop, and the SDK version constant must stay synchronized
/// with what CsWin32 metadata declares.
/// </summary>
internal static class D3D11Devices
{
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2050:Correctness of COM interop cannot be guaranteed after trimming.",
        Justification = "ADR-0010: Platform.Windows is IsAotCompatible=false by design; D3D11 is the COM-boundary contract."
    )]
    public static unsafe ID3D11Device CreateBgra(LoggerHandle log)
    {
        var featureLevels = stackalloc D3D_FEATURE_LEVEL[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
        };
        D3D_FEATURE_LEVEL chosenLevel;
        var hr = PInvoke.D3D11CreateDevice(
            pAdapter: null,
            DriverType: D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            Software: default,
            Flags: D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT
                | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_SINGLETHREADED,
            pFeatureLevels: featureLevels,
            FeatureLevels: 2,
            SDKVersion: 7,
            ppDevice: out var d3dDevice,
            pFeatureLevel: &chosenLevel,
            ppImmediateContext: out _
        );
        hr.ThrowOnFailure();
        log.Debug("D3D11 device created", new LogField("feature_level", chosenLevel.ToString()));
        return d3dDevice!;
    }
}
