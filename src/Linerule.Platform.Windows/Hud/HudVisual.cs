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

    /// <summary>
    /// Floor on the HUD redraw cadence. The D2D + DirectWrite path
    /// behind <see cref="HudRenderer.Draw"/> opens a fresh
    /// <c>ID2D1DeviceContext</c>, mints brushes, and creates per-call
    /// <c>IDWriteTextLayout</c>s; doing this on every hotkey-driven
    /// state change (≥ 80 Hz under long-press auto-repeat) showed up as
    /// a hard process exit with no managed stack — likely a GPU
    /// resource pressure cliff. 33 ms ≈ 30 Hz is well above the human
    /// "I can read changing numbers" threshold and stays well within
    /// the D2D resource budget.
    /// </summary>
    private const long MinDrawIntervalMs = 33;

    private readonly ContainerVisual _parent;
    private readonly HudRenderer _renderer;
    private readonly SpriteVisual _visual;
    private readonly CompositionSurfaceBrush _brush;
    private readonly HudLayout _layout;
    private HudContent? _last;
    private long _lastDrawAtMs;
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
    ///
    /// <para>
    /// When opacity falls below <see cref="CullThreshold"/> the visual is
    /// also marked <c>IsVisible=false</c>, which tells DComp to skip the
    /// HUD subtree entirely during the next compose pass — zero pixel
    /// work, zero shader work. Above the threshold the visual is
    /// re-enabled. This matches the user expectation that a fully
    /// transparent HUD should not be silently consuming GPU budget
    /// (2026-05-11 feedback).
    /// </para>
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
        _visual.IsVisible = clamped > CullThreshold;
        _lastOpacity = clamped;
    }

    private const float CullThreshold = 0.01f;

    /// <summary>
    /// Re-render iff <paramref name="content"/> differs from the last
    /// rendered payload AND we are outside the 33 ms throttle window.
    /// Skipped updates are dropped, not queued — the TickLoop's wall-clock
    /// telemetry refresh (every 200 ms) guarantees eventual consistency
    /// of the visible HUD with the actual state.
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
        var nowMs = Environment.TickCount64;
        if (nowMs - _lastDrawAtMs < MinDrawIntervalMs)
        {
            return;
        }
        _renderer.Draw(content);
        _last = content;
        _lastDrawAtMs = nowMs;
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
