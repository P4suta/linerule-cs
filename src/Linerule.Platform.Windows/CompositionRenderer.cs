using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Linerule.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Linerule.Platform.Windows;

/// <summary>
/// Maps an <see cref="OverlayFrame"/> onto a parent dcomp visual's
/// children. Pools <see cref="IDCompositionVisual2"/> + per-layer
/// <see cref="IDCompositionSurface"/> pairs across frames; each surface
/// is sized to its layer's rect and re-drawn (clear + solid fill)
/// only when color or size actually changes.
///
/// <para>
/// ADR-0010 Phase 2 / ADR-0009 dcomp-direct: the previous
/// <c>SpriteVisual + CompositionColorBrush</c> pattern from
/// <c>Windows.UI.Composition</c> has no equivalent in raw dcomp — there
/// is no first-class color brush primitive. We instead allocate a
/// per-layer surface filled by D2D <c>FillRectangle</c>; the runtime cost
/// is one surface allocation per resize and one D2D draw per recolor,
/// versus zero for the brush mutation. Acceptable for the overlay's
/// 1–4 layer count.
/// </para>
///
/// <para>
/// <b>Why direct snap</b> remains correct: history — 35-120 ms easing at
/// a 60 Hz tick trailed the cursor 2-3 frames; 8 ms easing at 120 Hz
/// read as ~10 FPS. Smoothness has to come from VSync-aligned tick
/// scheduling (RenderClock + DwmFlush), not from easing.
/// </para>
/// </summary>
internal sealed class CompositionRenderer(IDCompositionDesktopDevice device, IDCompositionVisual2 root) : IDisposable
{
    private readonly IDCompositionDesktopDevice _device = device;
    private readonly IDCompositionVisual2 _root = root;
    private readonly List<PooledLayer> _pool = new(capacity: 4);
    private bool _disposed;

    public void Apply(OverlayFrame frame)
    {
        if (_disposed)
        {
            return;
        }
        var n = frame.LayerCount;

        while (_pool.Count < n)
        {
            _device.CreateVisual(out var visual);
            _root.AddVisual(visual, insertAbove: true, referenceVisual: null);
            _pool.Add(new PooledLayer(Visual: visual));
        }

        while (_pool.Count > n)
        {
            var idx = _pool.Count - 1;
            var pooled = _pool[idx];
            _root.RemoveVisual(pooled.Visual);
            DisposeLayer(pooled);
            _pool.RemoveAt(idx);
        }

        for (var i = 0; i < n; i++)
        {
            UpdateLayer(_pool[i], frame.Layers[i], i, out var updated);
            if (
                !ReferenceEquals(updated.Surface, _pool[i].Surface)
                || updated.LastColor != _pool[i].LastColor
                || updated.LastWidth != _pool[i].LastWidth
                || updated.LastHeight != _pool[i].LastHeight
            )
            {
                _pool[i] = updated;
            }
        }
    }

    private void UpdateLayer(PooledLayer pooled, Layer layer, int index, out PooledLayer next)
    {
        var (rect, color) = Translate(layer);
        var w = (uint)Math.Max(1, (int)rect.Width);
        var h = (uint)Math.Max(1, (int)rect.Height);

        pooled.Visual.SetOffsetX(rect.X);
        pooled.Visual.SetOffsetY(rect.Y);

        var surface = pooled.Surface;
        var sizeChanged = w != pooled.LastWidth || h != pooled.LastHeight;
        if (surface is null || sizeChanged)
        {
            if (surface is not null)
            {
                ComLifetime.Release(surface);
            }
            _device.CreateSurface(
                w,
                h,
                DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
                out surface
            );
            pooled.Visual.SetContent(surface);
        }

        var colorChanged = pooled.Surface is null || sizeChanged || pooled.LastColor != color;
        if (colorChanged && surface is not null)
        {
            FillSurface(surface, color, w, h);
        }

        next = pooled with { Surface = surface, LastWidth = w, LastHeight = h, LastColor = color };
        _ = index;
    }

    /// <summary>
    /// Begin-draw the dcomp surface, clear, and fill with the given color
    /// using D2D. The surface's <c>BeginDraw</c> hands back an
    /// <see cref="ID2D1DeviceContext"/> already marshaled as an
    /// <see langword="object"/>, plus the offset where the update should
    /// land — D2D transform is set to identity-with-translation so we draw
    /// at the origin of the update region.
    /// </summary>
    private static unsafe void FillSurface(IDCompositionSurface surface, Rgba color, uint width, uint height)
    {
        var iid = IID_ID2D1DeviceContext;
        System.Drawing.Point offset = default;
        surface.BeginDraw(updateRect: null, iid: &iid, updateObject: out var ctxObj, updateOffset: &offset);
        var ctx = (ID2D1DeviceContext)ctxObj;
        try
        {
            var transform = default(D2D_MATRIX_3X2_F);
            transform.Anonymous.Anonymous1.m11 = 1f;
            transform.Anonymous.Anonymous1.m22 = 1f;
            transform.Anonymous.Anonymous1.dx = offset.X;
            transform.Anonymous.Anonymous1.dy = offset.Y;
            ctx.SetTransform(transform);
            ctx.Clear(Transparent);
            var rect = new D2D_RECT_F
            {
                left = 0f,
                top = 0f,
                right = width,
                bottom = height,
            };
            var brushColor = new D2D1_COLOR_F
            {
                r = color.R / 255f,
                g = color.G / 255f,
                b = color.B / 255f,
                a = color.A / 255f,
            };
            ctx.CreateSolidColorBrush(in brushColor, brushProperties: null, out var brush);
            try
            {
                ctx.FillRectangle(in rect, brush);
            }
            finally
            {
                ComLifetime.Release(brush);
            }
        }
        finally
        {
            ComLifetime.Release(ctx);
            surface.EndDraw();
        }
    }

    private static readonly D2D1_COLOR_F Transparent = new()
    {
        r = 0f,
        g = 0f,
        b = 0f,
        a = 0f,
    };

    // IID for ID2D1DeviceContext (D2D 1.1) — passed to BeginDraw to request
    // a D2D context to draw into the dcomp surface.
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

    private static ((float X, float Y, float Width, float Height) Rect, Rgba Color) Translate(Layer layer)
    {
        var rect = layer.Geometry switch
        {
            Geometry.Rect r => (
                X: (float)r.Bounds.Left,
                Y: (float)r.Bounds.Top,
                Width: (float)r.Bounds.Width,
                Height: (float)r.Bounds.Height
            ),
            _ => throw new System.Diagnostics.UnreachableException("unknown geometry variant"),
        };

        var color = layer.Brush switch
        {
            Brush.Solid s => s.Color,
            _ => throw new System.Diagnostics.UnreachableException("unknown brush variant"),
        };

        return (rect, color);
    }

    private static void DisposeLayer(PooledLayer pooled)
    {
        if (pooled.Surface is not null)
        {
            ComLifetime.Release(pooled.Surface);
        }
        ComLifetime.Release(pooled.Visual);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var pooled in _pool)
        {
            DisposeLayer(pooled);
        }
        _pool.Clear();
    }

    /// <summary>One pooled visual + its current sized + colored surface.</summary>
    private sealed record PooledLayer(IDCompositionVisual2 Visual)
    {
        public IDCompositionSurface? Surface { get; init; }
        public uint LastWidth { get; init; }
        public uint LastHeight { get; init; }
        public Rgba LastColor { get; init; }
    }
}
