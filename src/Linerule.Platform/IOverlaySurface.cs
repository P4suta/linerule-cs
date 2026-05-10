using Linerule.Core;

namespace Linerule.Platform;

/// <summary>
/// Abstract overlay surface. The pure render function produces an
/// <see cref="OverlayFrame"/>; the platform layer maps it onto an OS-specific
/// composition / draw surface.
/// </summary>
public interface IOverlaySurface : IAsyncDisposable
{
    /// <summary>Logical-pixel bounds of the monitor the overlay covers.</summary>
    ScreenRect<Logical> MonitorBounds { get; }

    /// <summary>
    /// Replace the surface's current content with <paramref name="frame"/>.
    /// Implementations should diff cheaply; the renderer emits 0–2 layers in v0.1.
    /// </summary>
    void Apply(OverlayFrame frame);
}
