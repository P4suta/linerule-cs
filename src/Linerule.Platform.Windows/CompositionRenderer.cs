using System.Numerics;
using Linerule.Core;
using Windows.UI;
using Windows.UI.Composition;

namespace Linerule.Platform.Windows;

/// <summary>
/// Maps an <see cref="OverlayFrame"/> onto a <see cref="ContainerVisual"/>'s
/// children. Pools <see cref="SpriteVisual"/>s across frames; each pooled
/// visual carries a per-instance <see cref="CompositionColorBrush"/> that is
/// re-colored in place rather than re-allocated.
///
/// <para>
/// Uses <see cref="Windows.UI.Composition"/> via the
/// <c>ICompositorDesktopInterop</c> path (the PowerToys MouseHighlighter
/// pattern — the canonical click-through transparent overlay route on
/// Windows in 2026; see ADR-0009 v3). The overlay's
/// <see cref="Windows.UI.Composition.Desktop.DesktopWindowTarget"/> hosts
/// the visual tree; the same compositor is shared with the Win2D-painted
/// HUD so both render through one device.
/// </para>
///
/// <para>
/// <b>Why direct snap</b>: history — 35-120 ms easing at a 60 Hz tick
/// trailed the cursor 2-3 frames (eased durations longer than the tick
/// interval kept cascading). 8 ms easing at the matching 120 Hz tick rate
/// was tried next (2026-05-11) and read as ~10 FPS, far worse than the
/// snap: each tick re-triggers the animation from the current
/// <i>animated</i> value, so the GPU compositor never lets a value settle.
/// Smoothness has to come from VSync-aligned tick scheduling, not from
/// easing — easing of any duration overlapping the tick rate is wrong.
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
        // Direct snap — the cursor poll IS the refresh tick; the rendered
        // position should match it 1:1. Implicit animations were tried at
        // 8 ms linear (matching the tick interval) but felt closer to 10 FPS
        // than 120 (verified 2026-05-11 user run): each tick re-triggers
        // the animation from the current animated value, so the GPU
        // compositor never lets a value settle — net visible motion is
        // worse than direct snap. Smoothness is to be solved by syncing
        // the tick to display vsync, not by easing.
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
