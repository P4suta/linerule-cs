using System.Numerics;
using Linerule.Core;
using Windows.UI;
using Windows.UI.Composition;

namespace Linerule.Platform.Windows;

/// <summary>
/// Maps an <see cref="OverlayFrame"/> onto a <see cref="ContainerVisual"/>'s
/// children. Pools <see cref="SpriteVisual"/>s across frames; each pooled
/// visual carries an <see cref="ImplicitAnimationCollection"/> so subsequent
/// <c>Offset</c>/<c>Size</c> assignments smoothly interpolate on the GPU
/// compositor thread.
/// <para>
/// Uses <see cref="Windows.UI.Composition"/> (the OS-level WinRT Composition,
/// stable since Windows 10 1709), <em>not</em> <see cref="Microsoft.UI.Composition"/>:
/// <see cref="Microsoft.UI.Content.ContentIsland.CreateForSystemVisual"/> requires
/// <see cref="Windows.UI.Composition.Visual"/> at the root, per the WinAppSDK 1.7
/// release notes ("ContentIsland can use Windows.UI.Composition.Visuals for
/// rendering and Win32 window APIs for input processing"). This is what enables
/// pairing with <see cref="Microsoft.UI.Content.DesktopAttachedSiteBridge"/> and
/// its <c>ProcessesPointerInput = false</c> click-through knob.
/// </para>
/// </summary>
internal sealed class CompositionRenderer(Compositor compositor, ContainerVisual root)
{
    /// <summary>Animation duration for cursor-follow easing.</summary>
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(120);

    private readonly Compositor _compositor = compositor;
    private readonly ContainerVisual _root = root;
    private readonly ImplicitAnimationCollection _implicitAnimations = BuildImplicitAnimations(compositor);
    private readonly List<SpriteVisual> _pool = new(capacity: 4);

    public void Apply(OverlayFrame frame)
    {
        var n = frame.LayerCount;

        while (_pool.Count < n)
        {
            var v = _compositor.CreateSpriteVisual();
            v.ImplicitAnimations = _implicitAnimations;
            _root.Children.InsertAtTop(v);
            _pool.Add(v);
        }

        while (_pool.Count > n)
        {
            var idx = _pool.Count - 1;
            _root.Children.Remove(_pool[idx]);
            _pool.RemoveAt(idx);
        }

        for (var i = 0; i < n; i++)
        {
            UpdateVisual(_pool[i], frame.Layers[i]);
        }
    }

    private void UpdateVisual(SpriteVisual visual, Layer layer)
    {
        var (rect, color) = Translate(layer);
        // Setting Offset/Size triggers the implicit animations on those
        // properties — the compositor thread interpolates from current to
        // target over AnimationDuration, decoupled from the UI tick.
        visual.Offset = new Vector3(rect.X, rect.Y, 0);
        visual.Size = new Vector2(rect.Width, rect.Height);
        visual.Brush = _compositor.CreateColorBrush(color);
    }

    private static ImplicitAnimationCollection BuildImplicitAnimations(Compositor compositor)
    {
        var offsetAnim = compositor.CreateVector3KeyFrameAnimation();
        offsetAnim.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
        offsetAnim.Duration = AnimationDuration;
        offsetAnim.Target = nameof(SpriteVisual.Offset);

        var sizeAnim = compositor.CreateVector2KeyFrameAnimation();
        sizeAnim.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
        sizeAnim.Duration = AnimationDuration;
        sizeAnim.Target = nameof(SpriteVisual.Size);

        var collection = compositor.CreateImplicitAnimationCollection();
        collection[nameof(SpriteVisual.Offset)] = offsetAnim;
        collection[nameof(SpriteVisual.Size)] = sizeAnim;
        return collection;
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
}
