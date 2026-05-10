using System;
using System.Runtime.InteropServices;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Immutable summary of a <see cref="RenderStats"/> rolling window.
/// All-zero shape (<see cref="Empty"/>) is the published "no samples yet"
/// sentinel — callers shouldn't compute anything from it.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RenderStatsSnapshot(
    int SampleCount,
    long TotalFrames,
    long DroppedFrames,
    TimeSpan Min,
    TimeSpan Mean,
    TimeSpan P50,
    TimeSpan P95,
    TimeSpan P99,
    TimeSpan Max
)
{
    public static RenderStatsSnapshot Empty { get; } =
        new(0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}
