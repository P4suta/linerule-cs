using System;
using System.Globalization;
using System.Numerics;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Linerule.Platform.Windows.Hud;

/// <summary>
/// Win2D-backed HUD painter. Owns the <see cref="CanvasDevice"/>,
/// <see cref="CompositionGraphicsDevice"/>, the
/// <see cref="CompositionDrawingSurface"/> that the visual brush samples,
/// and the four <see cref="CanvasTextFormat"/>s (title / status / body /
/// telemetry).
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
/// <b>Allocations</b>: <see cref="Draw"/> opens one drawing session,
/// fills text with cached <see cref="CanvasTextFormat"/>s. The text
/// strings themselves are formatted per call (~10 small string allocs
/// per Draw); call sites throttle Draw via content equality so the
/// steady-state HUD churn is one Draw per actual content change.
/// </para>
/// </summary>
internal sealed partial class HudRenderer : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Hud);
    private static readonly Color Background = Color.FromArgb(0xD8, 0x10, 0x12, 0x18);
    private static readonly Color Foreground = Color.FromArgb(0xFF, 0xE6, 0xE9, 0xEF);
    private static readonly Color Subtle = Color.FromArgb(0xFF, 0x9A, 0xA3, 0xB2);
    private static readonly Color Accent = Color.FromArgb(0xFF, 0xFF, 0xC2, 0x4D);
    private static readonly Color Hint = Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B);
    private static readonly Color Divider = Color.FromArgb(0x40, 0xE6, 0xE9, 0xEF);

    private const string MonoFontFamily = "Cascadia Code, Consolas, Courier New";
    private const string TitleFontFamily = "Segoe UI Variable, Segoe UI, sans-serif";

    private readonly HudLayout _layout;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private readonly CanvasTextFormat _titleFormat;
    private readonly CanvasTextFormat _statusFormat;
    private readonly CanvasTextFormat _bodyFormat;
    private readonly CanvasTextFormat _telemetryFormat;
    private bool _disposed;

    public CompositionDrawingSurface Surface { get; }

    public HudRenderer(Compositor compositor, HudLayout layout)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        _layout = layout;

        // CanvasDevice is reference-counted and shared across the process —
        // GetSharedDevice() doesn't transfer ownership, so we don't keep
        // a field for it (CA2213). The graphics device wraps the shared
        // device and IS owned + disposed by us.
        var canvasDevice = CanvasDevice.GetSharedDevice();
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvasDevice);
        Surface = _graphicsDevice.CreateDrawingSurface(
            new Size(layout.SizePx.X, layout.SizePx.Y),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied
        );

        _titleFormat = MakeFormat(TitleFontFamily, layout.FontSizes.Title, FontWeights.SemiBold);
        _statusFormat = MakeFormat(MonoFontFamily, layout.FontSizes.Status, FontWeights.Normal);
        _bodyFormat = MakeFormat(MonoFontFamily, layout.FontSizes.Body, FontWeights.Normal);
        _telemetryFormat = MakeFormat(MonoFontFamily, layout.FontSizes.Telemetry, FontWeights.Normal);

        Log.Debug(
            "HudRenderer constructed",
            new LogField("size_w", layout.SizePx.X),
            new LogField("size_h", layout.SizePx.Y)
        );
    }

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
        using var session = CanvasComposition.CreateDrawingSession(Surface);
        session.Clear(Colors.Transparent);

        var w = _layout.SizePx.X;
        var h = _layout.SizePx.Y;
        var corner = 10f * _layout.DpiScale;

        session.FillRoundedRectangle(0, 0, w, h, corner, corner, Background);

        var x = _layout.Padding.Edge;
        var y = _layout.Padding.Edge;

        y = DrawTitle(session, x, y);
        y = DrawDivider(session, x, y);
        y = DrawStatus(session, x, y, content.Status);
        y = DrawDivider(session, x, y);
        y = DrawHotkeys(session, x, y, content.Hotkeys);
        y = DrawDivider(session, x, y);
        y = DrawTelemetry(session, x, y, content.Telemetry);
        if (content.Hint is { Length: > 0 } hint)
        {
            y = DrawDivider(session, x, y);
            DrawHint(session, x, y, hint);
        }
    }

    private float DrawTitle(CanvasDrawingSession session, float x, float y)
    {
        session.DrawText("linerule", new Vector2(x, y), Accent, _titleFormat);
        return y + _layout.FontSizes.Title + _layout.Padding.Section;
    }

    private float DrawDivider(CanvasDrawingSession session, float x, float y)
    {
        var lineY = y + (_layout.Padding.Section / 2);
        session.DrawLine(
            new Vector2(x, lineY),
            new Vector2(_layout.SizePx.X - _layout.Padding.Edge, lineY),
            Divider,
            1f
        );
        return y + _layout.Padding.Section;
    }

    private float DrawStatus(CanvasDrawingSession session, float x, float y, HudStatus status)
    {
        y = DrawKeyValue(session, x, y, "Mode", status.Mode);
        y = DrawKeyValue(session, x, y, "Visible", status.Visible ? "yes" : "no");
        y = DrawKeyValue(
            session,
            x,
            y,
            "Opacity",
            string.Create(CultureInfo.InvariantCulture, $"{status.OpacityPercent}%")
        );
        y = DrawKeyValue(
            session,
            x,
            y,
            "Thickness",
            string.Create(CultureInfo.InvariantCulture, $"{status.ThicknessPx} px")
        );
        return y + (_layout.Padding.Section / 2);
    }

    private float DrawHotkeys(
        CanvasDrawingSession session,
        float x,
        float y,
        System.Collections.Immutable.ImmutableArray<HudHotkeyRow> rows
    )
    {
        foreach (var row in rows)
        {
            session.DrawText(row.Chord, new Vector2(x, y), Accent, _bodyFormat);
            DrawRightAligned(session, _layout.SizePx.X - _layout.Padding.Edge, y, row.Label, Foreground, _bodyFormat);
            y += _layout.FontSizes.Body + _layout.Padding.Row;
        }
        return y + (_layout.Padding.Section / 2);
    }

    private float DrawTelemetry(CanvasDrawingSession session, float x, float y, HudTelemetry t)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{t.DisplayHz}Hz · tick {t.TickHz}Hz · p99 {t.TickP99Ms:F2}ms · drops {t.FramesDropped}"
        );
        session.DrawText(line, new Vector2(x, y), Subtle, _telemetryFormat);
        return y + _layout.FontSizes.Telemetry + _layout.Padding.Section;
    }

    private void DrawHint(CanvasDrawingSession session, float x, float y, string text)
    {
        session.DrawText(text, new Vector2(x, y), Hint, _telemetryFormat);
    }

    private float DrawKeyValue(CanvasDrawingSession session, float x, float y, string key, string value)
    {
        session.DrawText(key, new Vector2(x, y), Subtle, _statusFormat);
        DrawRightAligned(session, _layout.SizePx.X - _layout.Padding.Edge, y, value, Foreground, _statusFormat);
        return y + _layout.FontSizes.Status + _layout.Padding.Row;
    }

    private static void DrawRightAligned(
        CanvasDrawingSession session,
        float rightEdge,
        float y,
        string text,
        Color color,
        CanvasTextFormat format
    )
    {
        using var layout = new CanvasTextLayout(session, text, format, 0f, 0f);
        var textWidth = (float)layout.LayoutBounds.Width;
        session.DrawText(text, new Vector2(rightEdge - textWidth, y), color, format);
    }

    private static CanvasTextFormat MakeFormat(string family, float sizePx, FontWeight weight) =>
        new()
        {
            FontFamily = family,
            FontSize = sizePx,
            FontWeight = weight,
            WordWrapping = CanvasWordWrapping.NoWrap,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _titleFormat.Dispose();
        _statusFormat.Dispose();
        _bodyFormat.Dispose();
        _telemetryFormat.Dispose();
        Surface.Dispose();
        _graphicsDevice.Dispose();
        // The CanvasDevice we used during construction is the shared one
        // (CanvasDevice.GetSharedDevice) — owned by Win2D, must not be disposed here.
    }

    /// <summary>Local FontWeight constants — Win2D's FontWeight is a struct.</summary>
    private static class FontWeights
    {
        public static readonly FontWeight Normal = new() { Weight = 400 };
        public static readonly FontWeight SemiBold = new() { Weight = 600 };
    }
}
