using System;
using System.Runtime.InteropServices;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Display-refresh-derived render timing. All four fields are causally
/// linked — change one and the rest fall out — so they live together as
/// an immutable record. Construct via <see cref="Probe"/> rather than the
/// primary constructor unless you need to inject a synthetic value (tests).
///
/// <para>
/// <b>Rationale</b>: previous iterations hard-coded 16 ms then 8 ms tick
/// intervals. Both ignored the actual display refresh rate, so on a 144 Hz
/// monitor the overlay was visibly choppy and on a 60 Hz monitor it
/// over-polled. User feedback 2026-05-11 ("ガクガクしている") was the trigger
/// to lift this out of magic-number territory.
/// </para>
///
/// <para>
/// <b>Tick rate policy</b>: tick at <c>min(2 × refresh, 250) Hz</c>, so we
/// never miss a vsync (Nyquist) but stay below the rate at which
/// <see cref="System.Diagnostics.Stopwatch"/> jitter and DispatcherQueueTimer
/// granularity start to dominate. Cap at 250 Hz because 240 Hz monitors
/// exist and we want one extra cycle of headroom.
/// </para>
///
/// <para>
/// <b>Frame budget</b>: <c>1 / refresh</c>. A tick that exceeds half this
/// budget is logged as a warning by <see cref="RenderBudget"/> — that is
/// the threshold at which we are at risk of dropping the next display frame.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RenderTiming(
    int DisplayRefreshHz,
    int TickRateHz,
    TimeSpan TickInterval,
    TimeSpan FrameBudget
)
{
    private const int FallbackRefreshHz = 60;
    private const int TickMultiplier = 2;
    private const int TickRateCapHz = 250;

    /// <summary>
    /// Query the primary display's refresh rate and derive the tick + budget
    /// numbers. Falls back to 60 Hz if the OS query fails.
    /// </summary>
    public static RenderTiming Probe(LoggerHandle log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var refreshHz = ProbePrimaryRefreshHz(log);
        return ForRefreshHz(refreshHz);
    }

    /// <summary>
    /// Build the timing tuple from a known refresh rate. Public for tests
    /// and for callers that want to override the probe (e.g. force a 60 Hz
    /// tick in CI).
    /// </summary>
    public static RenderTiming ForRefreshHz(int refreshHz)
    {
        var hz = refreshHz < 1 ? FallbackRefreshHz : refreshHz;
        var tickHz = Math.Min(hz * TickMultiplier, TickRateCapHz);
        return new RenderTiming(
            DisplayRefreshHz: hz,
            TickRateHz: tickHz,
            TickInterval: TimeSpan.FromMilliseconds(1000.0 / tickHz),
            FrameBudget: TimeSpan.FromMilliseconds(1000.0 / hz)
        );
    }

    private static unsafe int ProbePrimaryRefreshHz(LoggerHandle log)
    {
        var devMode = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
        // Passing null for the device name selects the primary display.
        var ok = PInvoke.EnumDisplaySettings(
            lpszDeviceName: null,
            iModeNum: ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS,
            lpDevMode: ref devMode
        );
        if (!ok)
        {
            var (err, name) = Win32Guard.LastError();
            log.Warn(
                "EnumDisplaySettings failed — falling back to 60 Hz",
                new LogField("err", err),
                new LogField("err_name", name)
            );
            return FallbackRefreshHz;
        }
        var hz = (int)devMode.dmDisplayFrequency;
        // 0 / 1 are documented as "default" / "lowest" — neither is a real
        // refresh rate. Treat as missing and fall back.
        if (hz <= 1)
        {
            log.Warn(
                "EnumDisplaySettings returned no usable refresh rate — falling back to 60 Hz",
                new LogField("raw_hz", hz)
            );
            return FallbackRefreshHz;
        }
        return hz;
    }
}
