using System;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;

namespace Linerule.Platform.Windows;

/// <summary>
/// Shared GPU device factories. The overlay's dcomp chain, HUD renderer, and
/// composition renderer all consume the same <see cref="ID3D11Device"/> +
/// <see cref="ID2D1Device"/> pair, created once in <see cref="WindowsApp"/>
/// and threaded through. Factoring keeps device-creation logic in one place —
/// BGRA_SUPPORT is required for D2D interop, and the dcomp device must be
/// minted with the D2D device as its rendering device so
/// <c>IDCompositionSurface::BeginDraw(IID_ID2D1DeviceContext)</c> is legal
/// (verified 2026-05-19 — first hardware test caught the previous DXGI-rooted
/// shape failing every frame with InvalidCastException). See ADR-0009 v4.
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2050:Correctness of COM interop cannot be guaranteed after trimming.",
        Justification = "ADR-0010: Platform.Windows is IsAotCompatible=false by design; D2D/DXGI is the COM-boundary contract."
    )]
    public static unsafe ID2D1Device CreateD2DDevice(ID3D11Device d3dDevice, LoggerHandle log)
    {
        ArgumentNullException.ThrowIfNull(d3dDevice);
        var dxgiDevice = (IDXGIDevice)d3dDevice;
        var factoryIid = typeof(ID2D1Factory1).GUID;
        PInvoke
            .D2D1CreateFactory(
                factoryType: D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                riid: &factoryIid,
                pFactoryOptions: null,
                ppIFactory: out var factoryObj
            )
            .ThrowOnFailure();
        var d2dFactory = (ID2D1Factory1)factoryObj;
        d2dFactory.CreateDevice(dxgiDevice, out var d2dDevice);
        log.Debug("D2D device created");
        return d2dDevice;
    }
}
