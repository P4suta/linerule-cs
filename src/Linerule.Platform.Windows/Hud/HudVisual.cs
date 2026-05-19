using System;
using System.Runtime.InteropServices;
using Linerule.Core;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32.Graphics.DirectComposition;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Composition wrapper for the HUD: owns the
/// <see cref="IDCompositionVisual2"/>, the
/// <see cref="IDCompositionSurface"/> the HUD draws into (managed by the
/// inner <see cref="HudRenderer"/>), an
/// <see cref="IDCompositionEffectGroup"/> for opacity control, and the
/// parent visual attachment. <see cref="Update"/> redraws iff content
/// changed (gated by 33 ms throttle); <see cref="SetOpacity"/> drives
/// the cursor-fade behavior.
///
/// <para>
/// <b>Why an EffectGroup for opacity</b>: dcomp visuals do not have a
/// direct <c>SetOpacity</c> property — that lives on
/// <see cref="IDCompositionEffectGroup"/>. We attach one effect group
/// per HUD visual via <c>visual.SetEffect(effectGroup)</c> and call
/// <c>effectGroup.SetOpacity(value)</c> when the cursor-fade kernel
/// produces a new value.
/// </para>
///
/// <para>
/// <b>Z-order</b>: the supplied parent is the overlay's foreground
/// container (<see cref="OverlayWindow.CreateLayer"/>), which sits above
/// the background container where <see cref="CompositionRenderer"/>
/// draws the focus-mode mask + stripes.
/// </para>
/// </summary>
internal sealed partial class HudVisual : IDisposable
{
    private readonly LoggerHandle _log;

    /// <summary>
    /// Floor on the HUD redraw cadence — see history note in PR.
    /// 33 ms ≈ 30 Hz is well above the human "I can read changing numbers"
    /// threshold and stays well within the D2D resource budget.
    /// </summary>
    private const long MinDrawIntervalMs = 33;

    private readonly IDCompositionVisual2 _parent;
    private readonly HudRenderer _renderer;
    private readonly IDCompositionVisual2 _visual;
    private readonly IDCompositionEffectGroup _effect;

    private readonly float _baseOpacity;

    private HudContent? _last;
    private long _lastDrawAtMs;
    private float _lastOpacity = 1f;
    private bool _disposed;

    /// <summary>HUD rectangle in HWND-pixel space.</summary>
    public HudLayout Layout { get; }

    public HudVisual(
        IDCompositionDesktopDevice device,
        IDCompositionVisual2 parent,
        HudLayout layout,
        HudConfig hudCfg,
        LoggerHandle log
    )
    {
        // GPU devices (ID3D11Device / ID2D1Device) are owned by WindowsApp and
        // already wired through `device` — the dcomp device was minted with
        // the D2D device as its rendering device, so BeginDraw pulls a fresh
        // ID2D1DeviceContext per frame without an explicit handle here.
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(hudCfg);
        _log = log;
        _parent = parent;
        Layout = layout;
        _baseOpacity = hudCfg.BaseOpacity;
        _renderer = new HudRenderer(device, layout, hudCfg, log);

        device.CreateVisual(out _visual);
        _visual.SetContent(_renderer.Surface);
        _visual.SetOffsetX(layout.PositionPx.X);
        _visual.SetOffsetY(layout.PositionPx.Y);

        device.CreateEffectGroup(out _effect);
        _effect.SetOpacity(_baseOpacity);
        _visual.SetEffect(_effect);

        _parent.AddVisual(_visual, insertAbove: true, referenceVisual: null);

        _log.Info(
            "HUD visual attached",
            new LogField("offset_x", layout.PositionPx.X),
            new LogField("offset_y", layout.PositionPx.Y),
            new LogField("size_w", layout.SizePx.X),
            new LogField("size_h", layout.SizePx.Y)
        );
    }

    /// <summary>
    /// Fade the entire HUD subtree. The supplied value is the cursor-driven
    /// fade factor in [0, 1]; the actual opacity written is
    /// <c>fade × <see cref="HudConfig.BaseOpacity"/></c>, so a fully visible
    /// HUD sits at the translucent base level rather than at 1.0.
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
        _effect.SetOpacity(clamped * _baseOpacity);
        _lastOpacity = clamped;
    }

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
            _parent.RemoveVisual(_visual);
        }
        catch (Exception ex)
        {
            _log.Warn(
                "HUD parent.RemoveVisual threw",
                new LogField("ex", ex.GetType().Name),
                new LogField("msg", ex.Message)
            );
        }
        Marshal.FinalReleaseComObject(_effect);
        Marshal.FinalReleaseComObject(_visual);
        _renderer.Dispose();
    }
}
