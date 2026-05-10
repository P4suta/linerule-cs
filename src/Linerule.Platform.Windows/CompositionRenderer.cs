using System.Numerics;
using Linerule.Core;
using Microsoft.UI.Composition;
using Windows.UI;

namespace Linerule.Platform.Windows;

/// <summary>
/// Maps an <see cref="OverlayFrame"/> onto a <see cref="ContainerVisual"/>'s
/// children. Pools <see cref="SpriteVisual"/>s across frames; each pooled
/// visual carries a per-instance <see cref="CompositionColorBrush"/> that is
/// re-colored in place rather than re-allocated.
///
/// <para>
/// Uses <see cref="Microsoft.UI.Composition"/> (the WinAppSDK Composition
/// stack — Microsoft's recommended path for new desktop apps in 2026).
/// The overlay's <see cref="Microsoft.UI.Content.ContentIsland.Create(Microsoft.UI.Composition.Visual)"/>
/// hosts the visual tree; the same compositor is shared with the Win2D-painted
/// HUD so both render through one device.
/// </para>
///
/// <para>
/// <b>Why no implicit animations any more</b>: a previous iteration set
/// <see cref="Visual.ImplicitAnimations"/> to a 35-120 ms eased curve so
/// micro-jitter on the mouse poll didn't show through. User feedback
/// 2026-05-11 ("ガクガクしている") flagged the resulting catch-up lag as
/// worse than the jitter — at 60 Hz tick + 35 ms easing, a new animation
/// begins every 16 ms before the previous one finishes, so the rendered
/// position is always trailing the cursor by 2-3 frames. Direct snap is
/// the right default for a reading ruler. If easing comes back later it
/// should be velocity-aware (only smooth slow motion).
/// </para>
///
/// <para>
/// <b>Why brushes are cached per visual</b>: <c>CreateColorBrush</c>
/// crosses the WinRT boundary and allocates a new GPU resource. Doing it
/// every frame on every layer (180/s at 60 Hz × 3 layers) burned visible
/// budget. Each <see cref="SpriteVisual"/> now keeps one brush whose
/// <see cref="CompositionColorBrush.Color"/> is mutated in place — zero
/// allocations on the steady-state path.
/// </para>
/// </summary>
internal sealed class CompositionRenderer(Compositor compositor, ContainerVisual root)
{
    private readonly Compositor _compositor = compositor;
    private readonly ContainerVisual _root = root;
    private readonly List<PooledVisual> _pool = new(capacity: 4);

    public void Apply(OverlayFrame frame)
    {
        var n = frame.LayerCount;

        while (_pool.Count < n)
        {
            var visual = _compositor.CreateSpriteVisual();
            var brush = _compositor.CreateColorBrush();
            visual.Brush = brush;
            _root.Children.InsertAtTop(visual);
            _pool.Add(new PooledVisual(visual, brush));
        }

        while (_pool.Count > n)
        {
            var idx = _pool.Count - 1;
            _root.Children.Remove(_pool[idx].Visual);
            _pool.RemoveAt(idx);
        }

        for (var i = 0; i < n; i++)
        {
            UpdateVisual(_pool[i], frame.Layers[i]);
        }
    }

    private static void UpdateVisual(PooledVisual pooled, Layer layer)
    {
        var (rect, color) = Translate(layer);
        // Direct snap — no implicit animation. The cursor poll IS the
        // refresh tick; the rendered position should match it 1:1.
        pooled.Visual.Offset = new Vector3(rect.X, rect.Y, 0);
        pooled.Visual.Size = new Vector2(rect.Width, rect.Height);
        // Recolor the existing brush in place — zero alloc on steady state.
        if (pooled.Brush.Color != color)
        {
            pooled.Brush.Color = color;
        }
    }

    private static ((float X, float Y, float Width, float Height) Rect, Color Color) Translate(Layer layer)
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
            Brush.Solid s => Color.FromArgb(s.Color.A, s.Color.R, s.Color.G, s.Color.B),
            _ => throw new System.Diagnostics.UnreachableException("unknown brush variant"),
        };

        return (rect, color);
    }

    /// <summary>One reusable visual + its in-place mutable color brush.</summary>
    private readonly record struct PooledVisual(SpriteVisual Visual, CompositionColorBrush Brush);
}
