using System;
using System.Numerics;
using Linerule.Platform.Windows.Diagnostics;
using Windows.UI.Composition;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Composition wrapper for the HUD: owns the
/// <see cref="SpriteVisual"/>, the <see cref="CompositionSurfaceBrush"/>
/// that samples <see cref="HudRenderer.Surface"/>, and the
/// <see cref="HudRenderer"/> itself. Public API is
/// <see cref="Update"/>: call it with new <see cref="HudContent"/>;
/// equality checks gate re-render so the steady-state cost is one
/// equality compare. <see cref="SetOpacity"/> lets the tick loop fade the
/// HUD as the reading-ruler highlight approaches it.
///
/// <para>
/// <b>Z-order</b>: the supplied parent is the overlay's foreground
/// container (<see cref="OverlayWindow.CreateLayer"/>), which sits above
/// the background container where <c>CompositionRenderer</c> draws the
/// focus-mode mask + stripes. The HUD therefore never gets dimmed by the
/// mask, regardless of insertion order on the background side.
/// </para>
/// </summary>
internal sealed partial class HudVisual : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Hud);

    private readonly ContainerVisual _parent;
    private readonly HudRenderer _renderer;
    private readonly SpriteVisual _visual;
    private readonly CompositionSurfaceBrush _brush;
    private readonly HudLayout _layout;
    private HudContent? _last;
    private float _lastOpacity = 1f;
    private bool _disposed;

    /// <summary>HUD rectangle in HWND-pixel space.</summary>
    public HudLayout Layout => _layout;

    public HudVisual(Compositor compositor, ContainerVisual parent, HudLayout layout)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent;
        _layout = layout;
        _renderer = new HudRenderer(compositor, layout);
        _brush = compositor.CreateSurfaceBrush(_renderer.Surface);
        _visual = compositor.CreateSpriteVisual();
        _visual.Brush = _brush;
        _visual.Offset = new Vector3(layout.PositionPx.X, layout.PositionPx.Y, 0);
        _visual.Size = layout.SizePx;
        _parent.Children.InsertAtTop(_visual);
        Log.Info(
            "HUD visual attached",
            new LogField("offset_x", layout.PositionPx.X),
            new LogField("offset_y", layout.PositionPx.Y),
            new LogField("size_w", layout.SizePx.X),
            new LogField("size_h", layout.SizePx.Y)
        );
    }

    /// <summary>
    /// Fade the entire HUD subtree by mutating the SpriteVisual's
    /// <see cref="Visual.Opacity"/>. Idempotent for unchanged values
    /// (saves a WinRT property write per tick on steady-state distance).
    /// Values are clamped to [0, 1].
    /// </summary>
    public void SetOpacity(float opacity)
    {
        if (_disposed)
        {
            return;
        }
        var clamped = Math.Clamp(opacity, 0f, 1f);
        if (MathF.Abs(clamped - _lastOpacity) < 0.001f)
        {
            return;
        }
        _visual.Opacity = clamped;
        _lastOpacity = clamped;
    }

    /// <summary>
    /// Re-render iff <paramref name="content"/> differs from the last
    /// rendered payload. Idempotent for unchanged content.
    /// </summary>
    public void Update(in HudContent content)
    {
        if (_disposed)
        {
            return;
        }
        if (_last is { } prev && prev.Equals(content))
        {
            return;
        }
        _renderer.Draw(content);
        _last = content;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _parent.Children.Remove(_visual);
        }
        catch (ObjectDisposedException)
        {
            // Parent visual was already torn down — happens by design on
            // clean shutdown because WindowsApp.ShutdownAsync disposes the
            // overlay (which closes its Composition tree) before this `using`
            // scope unwinds. Nothing to remove; not a warning.
        }
        catch (Exception ex)
        {
            Log.Warn("HUD parent.Remove threw", new LogField("ex", ex.GetType().Name), new LogField("msg", ex.Message));
        }
        _visual.Dispose();
        _brush.Dispose();
        _renderer.Dispose();
    }
}
