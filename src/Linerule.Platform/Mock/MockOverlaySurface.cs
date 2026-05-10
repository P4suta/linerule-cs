using System.Collections.Concurrent;
using Linerule.Core;

namespace Linerule.Platform.Mock;

/// <summary>Test double: records every applied frame so tests can assert against the trace.</summary>
public sealed class MockOverlaySurface : IOverlaySurface
{
    private readonly ConcurrentQueue<OverlayFrame> _frames = new();

    public required ScreenRect<Logical> MonitorBounds { get; init; }

    public IReadOnlyCollection<OverlayFrame> Frames => _frames;

    public void Apply(OverlayFrame frame) => _frames.Enqueue(frame);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
