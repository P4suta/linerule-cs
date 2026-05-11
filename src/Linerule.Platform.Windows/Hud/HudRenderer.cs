using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;
using Windows.Win32;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Direct2D + DirectWrite HUD painter sitting on a
/// <see cref="CompositionDrawingSurface"/>. All native types are
/// source-gen'd by <c>Microsoft.Windows.CsWin32</c> from
/// <c>NativeMethods.txt</c> — third-party D2D wrappers (Win2D, Vortice,
/// SharpDX, …) are deliberately not in the picture. See ADR-0009 v3.
///
/// <para>
/// <b>Device chain</b>:
/// <c>D3D11CreateDevice(BGRA_SUPPORT)</c> → <c>IDXGIDevice</c> →
/// <c>ID2D1Factory1.CreateDevice</c> → <c>ID2D1Device</c>. The device is
/// handed to <see cref="ICompositorInterop.CreateGraphicsDevice"/> which
/// returns a <see cref="CompositionGraphicsDevice"/>; that mints the
/// <see cref="CompositionDrawingSurface"/> the HUD draws into. Each
/// <see cref="Draw"/> opens a fresh <c>ID2D1DeviceContext</c> via
/// <see cref="ICompositionDrawingSurfaceInterop.BeginDraw"/>.
/// </para>
///
/// <para>
/// <b>Layout</b>: top of panel is a "linerule" title, then a horizontal
/// rule, then status rows (Mode / Visible / Opacity / Thickness), rule,
/// hotkey rows (chord on left, label on right), rule, telemetry footer
/// (display Hz / tick p99 / dropped frames). Optional hint at the bottom
/// in red if non-null. Background is a translucent dark rounded rect.
/// </para>
///
/// <para>
/// <b>Allocations</b>: <see cref="Draw"/> opens one drawing session and
/// builds 6 short-lived <c>ID2D1SolidColorBrush</c>es plus per-text
/// <c>IDWriteTextLayout</c> objects. Text strings themselves are formatted
/// per call (~10 small string allocs per Draw); HudVisual throttles Draw
/// via content equality so the steady-state HUD churn is one Draw per
/// actual content change.
/// </para>
/// </summary>
internal sealed partial class HudRenderer : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Hud);

    // D2D color constants (float RGBA, 0..1) — direct port of the old
    // Win2D Windows.UI.Color constants.
    private static readonly D2D1_COLOR_F Transparent = new()
    {
        r = 0f,
        g = 0f,
        b = 0f,
        a = 0f,
    };
    private static readonly D2D1_COLOR_F Background = new()
    {
        r = 0x10 / 255f,
        g = 0x12 / 255f,
        b = 0x18 / 255f,
        a = 0xD8 / 255f,
    };
    private static readonly D2D1_COLOR_F Foreground = new()
    {
        r = 0xE6 / 255f,
        g = 0xE9 / 255f,
        b = 0xEF / 255f,
        a = 1f,
    };
    private static readonly D2D1_COLOR_F Subtle = new()
    {
        r = 0x9A / 255f,
        g = 0xA3 / 255f,
        b = 0xB2 / 255f,
        a = 1f,
    };
    private static readonly D2D1_COLOR_F Accent = new()
    {
        r = 0xFF / 255f,
        g = 0xC2 / 255f,
        b = 0x4D / 255f,
        a = 1f,
    };
    private static readonly D2D1_COLOR_F Hint = new()
    {
        r = 0xFF / 255f,
        g = 0x6B / 255f,
        b = 0x6B / 255f,
        a = 1f,
    };
    private static readonly D2D1_COLOR_F Divider = new()
    {
        r = 0xE6 / 255f,
        g = 0xE9 / 255f,
        b = 0xEF / 255f,
        a = 0x40 / 255f,
    };

    // Single font family names — DirectWrite's IDWriteFactory.CreateTextFormat
    // expects exactly one face per call (it does NOT parse CSS-style
    // comma-separated fallback lists; an unrecognized name silently maps
    // to the system default, which is proportional — so the prior
    // "Cascadia Code, Consolas, Courier New" string was rendering as a
    // proportional substitute, which is why the hotkey rows didn't read
    // as monospace at all (user feedback 2026-05-11). Cascadia Mono
    // ships with Windows Terminal and Windows 11; Segoe UI is universal.
    private const string MonoFontFamily = "Cascadia Mono";
    private const string TitleFontFamily = "Segoe UI";

    // IID for ID2D1DeviceContext (D2D 1.1) — used as `iid` argument to
    // ICompositionDrawingSurfaceInterop.BeginDraw. Byte-array constructor
    // avoids string parsing per Meziantou MA0176.
    private static readonly Guid IID_ID2D1DeviceContext = new(
        0xe8f7fe7a,
        0x191c,
        0x466d,
        0xad,
        0x95,
        0x97,
        0x56,
        0x78,
        0xbd,
        0xa9,
        0x98
    );

    private readonly HudLayout _layout;
    private readonly ID3D11Device _d3dDevice;
    private readonly ID2D1Device _d2dDevice;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private readonly IDWriteFactory _dwriteFactory;
    private readonly IDWriteTextFormat _titleFormat;
    private readonly IDWriteTextFormat _statusFormat;
    private readonly IDWriteTextFormat _bodyFormat;
    private readonly IDWriteTextFormat _telemetryFormat;
    private bool _disposed;

    public CompositionDrawingSurface Surface { get; }

    public HudRenderer(Compositor compositor, HudLayout layout)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        _layout = layout;

        _d3dDevice = CreateD3D11Device();
        _d2dDevice = CreateD2DDevice(_d3dDevice);
        _graphicsDevice = CreateGraphicsDevice(compositor, _d2dDevice);
        Surface = CreateDrawingSurface(_graphicsDevice, layout);
        _dwriteFactory = CreateDWriteFactory();
        (_titleFormat, _statusFormat, _bodyFormat, _telemetryFormat) = CreateTextFormats(_dwriteFactory, layout);

        Log.Debug(
            "HudRenderer constructed",
            new LogField("size_w", layout.SizePx.X),
            new LogField("size_h", layout.SizePx.Y)
        );
    }

    private static unsafe ID3D11Device CreateD3D11Device()
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
        return d3dDevice!;
    }

    private static unsafe ID2D1Device CreateD2DDevice(ID3D11Device d3dDevice)
    {
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
        return d2dDevice;
    }

    private static CompositionGraphicsDevice CreateGraphicsDevice(Compositor compositor, ID2D1Device d2dDevice)
    {
        var compositorInterop = (ICompositorInterop)(object)compositor;
        var d2dDevicePtr = Marshal.GetIUnknownForObject(d2dDevice);
        IntPtr graphicsPtr;
        try
        {
            compositorInterop.CreateGraphicsDevice(d2dDevicePtr, out graphicsPtr);
        }
        finally
        {
            Marshal.Release(d2dDevicePtr);
        }
        try
        {
            // Wrap via CsWinRT — CompositionGraphicsDevice is a WinRT runtime
            // class, not a COM interface, so a direct managed cast off
            // Marshal.GetObjectForIUnknown would fail (InvalidCastException
            // against System.__ComObject — verified 2026-05-11).
            return global::WinRT.MarshalInspectable<CompositionGraphicsDevice>.FromAbi(graphicsPtr);
        }
        finally
        {
            Marshal.Release(graphicsPtr);
        }
    }

    private static CompositionDrawingSurface CreateDrawingSurface(CompositionGraphicsDevice device, HudLayout layout) =>
        // The system Windows.UI.Composition.CompositionGraphicsDevice.CreateDrawingSurface
        // takes Windows.Foundation.Size (float), unlike the Microsoft.UI variant
        // which uses Windows.Graphics.SizeInt32.
        device.CreateDrawingSurface(
            new Size(layout.SizePx.X, layout.SizePx.Y),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied
        );

    private static unsafe IDWriteFactory CreateDWriteFactory()
    {
        var dwriteIid = typeof(IDWriteFactory).GUID;
        PInvoke
            .DWriteCreateFactory(
                factoryType: DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                iid: &dwriteIid,
                factory: out var dwriteUnk
            )
            .ThrowOnFailure();
        return (IDWriteFactory)dwriteUnk;
    }

    private static (
        IDWriteTextFormat Title,
        IDWriteTextFormat Status,
        IDWriteTextFormat Body,
        IDWriteTextFormat Telemetry
    ) CreateTextFormats(IDWriteFactory dwriteFactory, HudLayout layout) =>
        (
            MakeFormat(
                dwriteFactory,
                TitleFontFamily,
                layout.FontSizes.Title,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD
            ),
            MakeFormat(
                dwriteFactory,
                MonoFontFamily,
                layout.FontSizes.Status,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
            ),
            MakeFormat(
                dwriteFactory,
                MonoFontFamily,
                layout.FontSizes.Body,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
            ),
            MakeFormat(
                dwriteFactory,
                MonoFontFamily,
                layout.FontSizes.Telemetry,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
            )
        );

    public void Draw(in HudContent content)
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            DrawCore(content);
        }
        catch (Exception ex)
        {
            Log.Warn(
                "HUD draw threw — keeping last frame",
                new LogField("ex", ex.GetType().Name),
                new LogField("msg", ex.Message)
            );
        }
    }

    private void DrawCore(in HudContent content)
    {
        var surfaceInterop = (ICompositionDrawingSurfaceInterop)(object)Surface;
        surfaceInterop.BeginDraw(IntPtr.Zero, IID_ID2D1DeviceContext, out var ctxPtr, out var offset);

        ID2D1DeviceContext? ctx = null;
        try
        {
            // GetUniqueObjectForIUnknown (vs GetObjectForIUnknown) returns
            // a fresh RCW per call, never a cached one. BeginDraw can in
            // principle hand back the same underlying device context
            // pointer across frames; with the cached-RCW variant the
            // wrapper would be reused after our ReleaseComObject below,
            // which races with the surface's own lifecycle on EndDraw.
            // Unique-RCW costs an allocation per frame but eliminates
            // that whole class of stale-wrapper hazards.
            ctx = (ID2D1DeviceContext)Marshal.GetUniqueObjectForIUnknown(ctxPtr);
            SetIdentityWithTranslation(ctx, offset);
            ctx.Clear(Transparent);
            PaintPanel(ctx, content);
        }
        finally
        {
            if (ctx is not null)
            {
                Marshal.ReleaseComObject(ctx);
            }
            surfaceInterop.EndDraw();
        }
    }

    private static void SetIdentityWithTranslation(ID2D1DeviceContext ctx, WinGraphicsPointInt32 offset)
    {
        var transform = default(D2D_MATRIX_3X2_F);
        transform.Anonymous.Anonymous1.m11 = 1f;
        transform.Anonymous.Anonymous1.m22 = 1f;
        transform.Anonymous.Anonymous1.dx = offset.X;
        transform.Anonymous.Anonymous1.dy = offset.Y;
        ctx.SetTransform(transform);
    }

    private void PaintPanel(ID2D1DeviceContext ctx, in HudContent content)
    {
        ctx.CreateSolidColorBrush(Background, brushProperties: null, out var bgBrush);
        ctx.CreateSolidColorBrush(Foreground, brushProperties: null, out var fgBrush);
        ctx.CreateSolidColorBrush(Subtle, brushProperties: null, out var subBrush);
        ctx.CreateSolidColorBrush(Accent, brushProperties: null, out var accBrush);
        ctx.CreateSolidColorBrush(Divider, brushProperties: null, out var divBrush);
        ctx.CreateSolidColorBrush(Hint, brushProperties: null, out var hintBrush);
        try
        {
            FillBackground(ctx, bgBrush);
            PaintRows(ctx, content, fgBrush, subBrush, accBrush, divBrush, hintBrush);
        }
        finally
        {
            Marshal.ReleaseComObject(hintBrush);
            Marshal.ReleaseComObject(divBrush);
            Marshal.ReleaseComObject(accBrush);
            Marshal.ReleaseComObject(subBrush);
            Marshal.ReleaseComObject(fgBrush);
            Marshal.ReleaseComObject(bgBrush);
        }
    }

    private void FillBackground(ID2D1DeviceContext ctx, ID2D1SolidColorBrush brush)
    {
        var w = _layout.SizePx.X;
        var h = _layout.SizePx.Y;
        var corner = 10f * _layout.DpiScale;
        ctx.FillRoundedRectangle(
            new D2D1_ROUNDED_RECT
            {
                rect = new D2D_RECT_F
                {
                    left = 0,
                    top = 0,
                    right = w,
                    bottom = h,
                },
                radiusX = corner,
                radiusY = corner,
            },
            brush
        );
    }

    private void PaintRows(
        ID2D1DeviceContext ctx,
        in HudContent content,
        ID2D1SolidColorBrush fgBrush,
        ID2D1SolidColorBrush subBrush,
        ID2D1SolidColorBrush accBrush,
        ID2D1SolidColorBrush divBrush,
        ID2D1SolidColorBrush hintBrush
    )
    {
        var x = _layout.Padding.Edge;
        var y = _layout.Padding.Edge;
        y = DrawTitle(ctx, accBrush, x, y);
        y = DrawDivider(ctx, divBrush, x, y);
        y = DrawStatus(ctx, subBrush, fgBrush, x, y, content.Status);
        y = DrawDivider(ctx, divBrush, x, y);
        y = DrawHotkeys(ctx, accBrush, fgBrush, x, y, content.Hotkeys);
        y = DrawDivider(ctx, divBrush, x, y);
        y = DrawTelemetry(ctx, subBrush, x, y, content.Telemetry);
        if (content.Hint is { Length: > 0 } hint)
        {
            y = DrawDivider(ctx, divBrush, x, y);
            DrawHint(ctx, hintBrush, x, y, hint);
        }
    }

    private float DrawTitle(ID2D1DeviceContext ctx, ID2D1SolidColorBrush brush, float x, float y)
    {
        DrawTextAt(ctx, brush, _titleFormat, x, y, "linerule");
        return y + _layout.FontSizes.Title + _layout.Padding.Section;
    }

    private float DrawDivider(ID2D1DeviceContext ctx, ID2D1SolidColorBrush brush, float x, float y)
    {
        var lineY = y + (_layout.Padding.Section / 2);
        ctx.DrawLine(
            new D2D_POINT_2F { x = x, y = lineY },
            new D2D_POINT_2F { x = _layout.SizePx.X - _layout.Padding.Edge, y = lineY },
            brush,
            strokeWidth: 1f,
            strokeStyle: null
        );
        return y + _layout.Padding.Section;
    }

    private float DrawStatus(
        ID2D1DeviceContext ctx,
        ID2D1SolidColorBrush keyBrush,
        ID2D1SolidColorBrush valueBrush,
        float x,
        float y,
        HudStatus status
    )
    {
        y = DrawKeyValue(ctx, keyBrush, valueBrush, x, y, "Mode", status.Mode);
        y = DrawKeyValue(ctx, keyBrush, valueBrush, x, y, "Visible", status.Visible ? "yes" : "no");
        y = DrawKeyValue(
            ctx,
            keyBrush,
            valueBrush,
            x,
            y,
            "Opacity",
            string.Create(CultureInfo.InvariantCulture, $"{status.OpacityPercent}%")
        );
        y = DrawKeyValue(
            ctx,
            keyBrush,
            valueBrush,
            x,
            y,
            "Thickness",
            string.Create(CultureInfo.InvariantCulture, $"{status.ThicknessPx} px")
        );
        return y + (_layout.Padding.Section / 2);
    }

    private float DrawHotkeys(
        ID2D1DeviceContext ctx,
        ID2D1SolidColorBrush chordBrush,
        ID2D1SolidColorBrush labelBrush,
        float x,
        float y,
        ImmutableArray<HudHotkeyRow> rows
    )
    {
        foreach (var row in rows)
        {
            DrawTextAt(ctx, chordBrush, _bodyFormat, x, y, row.Chord);
            DrawTextRightAligned(ctx, labelBrush, _bodyFormat, _layout.SizePx.X - _layout.Padding.Edge, y, row.Label);
            y += _layout.FontSizes.Body + _layout.Padding.Row;
        }
        return y + (_layout.Padding.Section / 2);
    }

    private float DrawTelemetry(ID2D1DeviceContext ctx, ID2D1SolidColorBrush brush, float x, float y, HudTelemetry t)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{t.DisplayHz}Hz · p99 {t.TickP99Ms:F2}ms · drops {t.FramesDropped} · stalls {t.CommitTimeouts}"
        );
        DrawTextAt(ctx, brush, _telemetryFormat, x, y, line);
        return y + _layout.FontSizes.Telemetry + _layout.Padding.Section;
    }

    private void DrawHint(ID2D1DeviceContext ctx, ID2D1SolidColorBrush brush, float x, float y, string text)
    {
        DrawTextAt(ctx, brush, _telemetryFormat, x, y, text);
    }

    private float DrawKeyValue(
        ID2D1DeviceContext ctx,
        ID2D1SolidColorBrush keyBrush,
        ID2D1SolidColorBrush valueBrush,
        float x,
        float y,
        string key,
        string value
    )
    {
        DrawTextAt(ctx, keyBrush, _statusFormat, x, y, key);
        DrawTextRightAligned(ctx, valueBrush, _statusFormat, _layout.SizePx.X - _layout.Padding.Edge, y, value);
        return y + _layout.FontSizes.Status + _layout.Padding.Row;
    }

    private void DrawTextAt(
        ID2D1DeviceContext ctx,
        ID2D1SolidColorBrush brush,
        IDWriteTextFormat format,
        float x,
        float y,
        string text
    )
    {
        _dwriteFactory.CreateTextLayout(
            @string: text,
            stringLength: (uint)text.Length,
            textFormat: format,
            maxWidth: float.MaxValue,
            maxHeight: float.MaxValue,
            textLayout: out var layout
        );
        try
        {
            ctx.DrawTextLayout(
                new D2D_POINT_2F { x = x, y = y },
                layout,
                brush,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE
            );
        }
        finally
        {
            Marshal.ReleaseComObject(layout);
        }
    }

    private void DrawTextRightAligned(
        ID2D1DeviceContext ctx,
        ID2D1SolidColorBrush brush,
        IDWriteTextFormat format,
        float rightEdge,
        float y,
        string text
    )
    {
        _dwriteFactory.CreateTextLayout(
            @string: text,
            stringLength: (uint)text.Length,
            textFormat: format,
            maxWidth: float.MaxValue,
            maxHeight: float.MaxValue,
            textLayout: out var layout
        );
        try
        {
            layout.GetMetrics(out var metrics);
            ctx.DrawTextLayout(
                new D2D_POINT_2F { x = rightEdge - metrics.width, y = y },
                layout,
                brush,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE
            );
        }
        finally
        {
            Marshal.ReleaseComObject(layout);
        }
    }

    private static IDWriteTextFormat MakeFormat(
        IDWriteFactory dwriteFactory,
        string family,
        float sizePx,
        DWRITE_FONT_WEIGHT weight
    )
    {
        dwriteFactory.CreateTextFormat(
            fontFamilyName: family,
            fontCollection: null,
            fontWeight: weight,
            fontStyle: DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            fontStretch: DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            fontSize: sizePx,
            localeName: "en-us",
            textFormat: out var fmt
        );
        fmt.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);
        fmt.SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING);
        fmt.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
        return fmt;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Marshal.ReleaseComObject(_titleFormat);
        Marshal.ReleaseComObject(_statusFormat);
        Marshal.ReleaseComObject(_bodyFormat);
        Marshal.ReleaseComObject(_telemetryFormat);
        Marshal.ReleaseComObject(_dwriteFactory);
        Surface.Dispose();
        _graphicsDevice.Dispose();
        Marshal.ReleaseComObject(_d2dDevice);
        Marshal.ReleaseComObject(_d3dDevice);
    }
}
