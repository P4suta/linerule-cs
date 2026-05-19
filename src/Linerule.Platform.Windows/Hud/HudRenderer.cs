using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Linerule.Core;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Direct2D + DirectWrite HUD painter sitting on a
/// <see cref="IDCompositionSurface"/>. All native types are
/// source-gen'd by <c>Microsoft.Windows.CsWin32</c> from
/// <c>NativeMethods.txt</c> — third-party D2D wrappers (Win2D, Vortice,
/// SharpDX, …) are deliberately not in the picture. See ADR-0009 (dcomp
/// direct, post-Phase-2) and ADR-0010.
///
/// <para>
/// <b>Device chain</b>:
/// <c>D3D11CreateDevice(BGRA_SUPPORT)</c> → <c>IDXGIDevice</c> →
/// <c>ID2D1Factory1.CreateDevice</c> → <c>ID2D1Device</c>. The HUD's
/// <see cref="IDCompositionSurface"/> is created directly from the
/// shared <see cref="IDCompositionDesktopDevice"/>. Each
/// <see cref="Draw"/> opens a fresh <c>ID2D1DeviceContext</c> via
/// <c>IDCompositionSurface.BeginDraw(IID_ID2D1DeviceContext, …)</c>.
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
    private readonly LoggerHandle _log;

    // Pure-clear color — independent of theme, never user-tunable.
    private static readonly D2D1_COLOR_F Transparent = new()
    {
        r = 0f,
        g = 0f,
        b = 0f,
        a = 0f,
    };

    // Theme colors are now instance fields sourced from HudConfig.Colors so
    // the user can re-skin the HUD via config.toml. DirectWrite font-family
    // names (single-name semantics — see feedback_directwrite_single_font_name)
    // are likewise instance-bound to HudConfig.Fonts.{TitleFamily,MonoFamily}.
    private readonly D2D1_COLOR_F _background;
    private readonly D2D1_COLOR_F _foreground;
    private readonly D2D1_COLOR_F _subtle;
    private readonly D2D1_COLOR_F _accent;
    private readonly D2D1_COLOR_F _hint;
    private readonly D2D1_COLOR_F _divider;

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
    private readonly IDWriteFactory _dwriteFactory;
    private readonly IDWriteTextFormat _titleFormat;
    private readonly IDWriteTextFormat _statusFormat;
    private readonly IDWriteTextFormat _bodyFormat;
    private readonly IDWriteTextFormat _telemetryFormat;
    private bool _disposed;

    public IDCompositionSurface Surface { get; }

    public HudRenderer(IDCompositionDesktopDevice device, HudLayout layout, HudConfig hudCfg, LoggerHandle log)
    {
        // The D2D device that minted `device` (the dcomp device) is owned by
        // WindowsApp — we don't accept it here because we never use it
        // directly. BeginDraw on the surfaces below pulls a fresh
        // ID2D1DeviceContext out of the dcomp device per frame; that works
        // iff the dcomp device was created with an ID2D1Device as its
        // rendering device (see D3D11Devices.CreateD2DDevice / ADR-0009).
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(hudCfg);
        _log = log;
        _layout = layout;

        _background = ToColorF(hudCfg.Colors.Background);
        _foreground = ToColorF(hudCfg.Colors.Foreground);
        _subtle = ToColorF(hudCfg.Colors.Subtle);
        _accent = ToColorF(hudCfg.Colors.Accent);
        _hint = ToColorF(hudCfg.Colors.Hint);
        _divider = ToColorF(hudCfg.Colors.Divider);

        Surface = CreateDcompSurface(device, layout);
        _dwriteFactory = CreateDWriteFactory();
        (_titleFormat, _statusFormat, _bodyFormat, _telemetryFormat) = CreateTextFormats(
            _dwriteFactory,
            layout,
            hudCfg.Fonts.TitleFamily,
            hudCfg.Fonts.MonoFamily
        );

        _log.Debug(
            "HudRenderer constructed",
            new LogField("size_w", layout.SizePx.X),
            new LogField("size_h", layout.SizePx.Y)
        );
    }

    private static D2D1_COLOR_F ToColorF(Rgba c) =>
        new()
        {
            r = c.R / 255f,
            g = c.G / 255f,
            b = c.B / 255f,
            a = c.A / 255f,
        };

    /// <summary>
    /// Mint the dcomp surface the HUD draws into. The size matches the HUD
    /// layout in physical pixels; the surface is BGRA8 / premultiplied to
    /// match the D2D device context produced by BeginDraw.
    /// </summary>
    private static IDCompositionSurface CreateDcompSurface(IDCompositionDesktopDevice device, HudLayout layout)
    {
        device.CreateSurface(
            (uint)Math.Max(1, layout.SizePx.X),
            (uint)Math.Max(1, layout.SizePx.Y),
            DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
            out var surface
        );
        return surface;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2050:Correctness of COM interop cannot be guaranteed after trimming.",
        Justification = "ADR-0010: Platform.Windows is IsAotCompatible=false by design; D3D11/D2D/DWrite is the COM-boundary contract."
    )]
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
    ) CreateTextFormats(IDWriteFactory dwriteFactory, HudLayout layout, string titleFamily, string monoFamily) =>
        (
            MakeFormat(
                dwriteFactory,
                titleFamily,
                layout.FontSizes.Title,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD
            ),
            MakeFormat(
                dwriteFactory,
                monoFamily,
                layout.FontSizes.Status,
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
            ),
            MakeFormat(dwriteFactory, monoFamily, layout.FontSizes.Body, DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL),
            MakeFormat(
                dwriteFactory,
                monoFamily,
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
            _log.Warn(
                "HUD draw threw — keeping last frame",
                new LogField("ex", ex.GetType().Name),
                new LogField("msg", ex.Message)
            );
        }
    }

    private unsafe void DrawCore(in HudContent content)
    {
        var iid = IID_ID2D1DeviceContext;
        System.Drawing.Point offset = default;
        // CsWin32 marshals the BeginDraw out parameter as object (the
        // [MarshalAs(IUnknown)] attribute is on the interface), so we get
        // a directly-castable RCW back — no GetUniqueObjectForIUnknown dance.
        Surface.BeginDraw(updateRect: null, iid: &iid, updateObject: out var ctxObj, updateOffset: &offset);
        var ctx = (ID2D1DeviceContext)ctxObj;
        try
        {
            SetIdentityWithTranslation(ctx, offset);
            ctx.Clear(Transparent);
            PaintPanel(ctx, content);
        }
        finally
        {
            Marshal.ReleaseComObject(ctx);
            Surface.EndDraw();
        }
    }

    private static void SetIdentityWithTranslation(ID2D1DeviceContext ctx, System.Drawing.Point offset)
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
        ctx.CreateSolidColorBrush(_background, brushProperties: null, out var bgBrush);
        ctx.CreateSolidColorBrush(_foreground, brushProperties: null, out var fgBrush);
        ctx.CreateSolidColorBrush(_subtle, brushProperties: null, out var subBrush);
        ctx.CreateSolidColorBrush(_accent, brushProperties: null, out var accBrush);
        ctx.CreateSolidColorBrush(_divider, brushProperties: null, out var divBrush);
        ctx.CreateSolidColorBrush(_hint, brushProperties: null, out var hintBrush);
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
        Marshal.FinalReleaseComObject(Surface);
        // The ID2D1Device that rooted the dcomp device is owned by WindowsApp
        // (shared with the dcomp device + CompositionRenderer); not released
        // here. ID3D11Device likewise.
    }
}
