using VerifyXunit;

namespace Linerule.Core.Tests.Snapshot;

public sealed class RenderSnapshot
{
    private static readonly ScreenRect<Logical> Monitor = new(new Point<Logical>(0, 0), 1920, 1080);

    private static readonly Point<Logical> Cursor = new(960, 540);

    [Fact]
    public Task Off_is_empty() => Verifier.Verify(Render.Frame(Mode.Off, Cursor, Monitor, OverlayConfig.Default));

    [Fact]
    public Task Horizontal_at_center() =>
        Verifier.Verify(Render.Frame(Mode.Horizontal, Cursor, Monitor, OverlayConfig.Default));

    [Fact]
    public Task Vertical_at_center() =>
        Verifier.Verify(Render.Frame(Mode.Vertical, Cursor, Monitor, OverlayConfig.Default));
}
