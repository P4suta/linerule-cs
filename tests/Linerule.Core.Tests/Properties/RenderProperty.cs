using FsCheck.Xunit;

namespace Linerule.Core.Tests.Properties;

public sealed class RenderProperty
{
    private static (Mode Mode, ScreenRect<Logical> Monitor, Point<Logical> Cursor) ShrinkInputs(
        byte modeIdx, ushort wRaw, ushort hRaw, int x, int y)
    {
        var mode = (Mode)(modeIdx % 3);
        var w = (uint)Math.Clamp((int)wRaw, 800, 4096);
        var h = (uint)Math.Clamp((int)hRaw, 600, 2160);
        var monitor = new ScreenRect<Logical>(new Point<Logical>(0, 0), w, h);
        var cursor = new Point<Logical>(x, y);
        return (mode, monitor, cursor);
    }

    [Property]
    public bool All_layers_fit_inside_monitor(byte modeIdx, ushort wRaw, ushort hRaw, int x, int y)
    {
        var (mode, monitor, cursor) = ShrinkInputs(modeIdx, wRaw, hRaw, x, y);
        var frame = Render.Frame(mode, cursor, monitor, OverlayConfig.Default);
        if (frame.IsEmpty)
        {
            return true;
        }

        foreach (var layer in frame.Layers)
        {
            var rect = ((Geometry.Rect)layer.Geometry).Bounds;
            if (!monitor.ContainsRect(rect))
            {
                return false;
            }
        }

        return true;
    }

    [Property]
    public bool Layer_count_matches_mode(byte modeIdx, ushort wRaw, ushort hRaw, int x, int y)
    {
        var (mode, monitor, cursor) = ShrinkInputs(modeIdx, wRaw, hRaw, x, y);
        var frame = Render.Frame(mode, cursor, monitor, OverlayConfig.Default);
        return mode switch
        {
            Mode.Off => frame.LayerCount == 0,
            Mode.Horizontal => frame.LayerCount == 3,
            Mode.Vertical => frame.LayerCount == 3,
            _ => false,
        };
    }
}
