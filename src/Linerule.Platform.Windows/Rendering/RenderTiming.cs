using System;
using System.Runtime.InteropServices;
using Linerule.Platform.Windows.Diagnostics;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Linerule.Platform.Windows.Rendering;

/// <summary>
/// Display-refresh-derived render timing. <see cref="DisplayRefreshHz"/>
/// is probed at startup; <see cref="FrameBudget"/> is its reciprocal and
/// drives the per-frame budget guard + the commit-await timeout ceiling.
///
/// <para>
/// <b>What this no longer carries</b>: the previous incarnation also held
/// <c>TickRateHz</c> + <c>TickInterval</c> to schedule a
/// <see cref="Microsoft.UI.Dispatching.DispatcherQueueTimer"/>. The tick
/// rate is no longer a policy — <see cref="RenderClock"/> drives ticks
/// off the compositor's own commit cycle via
/// <c>Compositor.RequestCommitAsync</c>, so the cadence equals the
/// display refresh by construction. Removing the field eliminates the
/// "what tick multiplier should we pick" tuning knob (which never had a
/// principled answer) and prevents the
/// tick-clock / display-clock phase drift that produced the
/// frame-to-frame jitter described in ADR-0009 v3.
/// </para>
///
/// <para>
/// <b>Frame budget</b>: <c>1 / refresh</c>. A tick that exceeds half this
/// budget is logged as a warning by <see cref="RenderBudget"/> — that is
/// the threshold at which we are at risk of dropping the next display
/// frame. The same value × 2 is reused as
/// <see cref="RenderClock"/>'s commit-await timeout, bounding any stall
/// (RDP / locked session / minimized) to two display frames.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct RenderTiming(int DisplayRefreshHz, TimeSpan FrameBudget)
{
    private const int DefaultFallbackRefreshHz = 60;

    /// <summary>
    /// Query the primary display's refresh rate and derive the budget.
    /// Falls back to <paramref name="fallbackHz"/> (default 60) if the OS query fails.
    /// </summary>
    public static RenderTiming Probe(LoggerHandle log, int fallbackHz = DefaultFallbackRefreshHz)
    {
        ArgumentNullException.ThrowIfNull(log);
        var refreshHz = ProbePrimaryRefreshHz(log, fallbackHz);
        return ForRefreshHz(refreshHz, fallbackHz);
    }

    /// <summary>
    /// Build the timing tuple from a known refresh rate. Public for tests
    /// and for callers that want to override the probe (e.g. force a 60 Hz
    /// budget in CI).
    /// </summary>
    public static RenderTiming ForRefreshHz(int refreshHz, int fallbackHz = DefaultFallbackRefreshHz)
    {
        var hz = refreshHz < 1 ? fallbackHz : refreshHz;
        // Integer division on TicksPerSecond avoids the `1000.0 / hz`
        // float round-trip — at 60 Hz the result is 166666 ticks
        // (= 16.6666 ms) exactly, with no double-precision residue that
        // could surface as a 100ns wobble in downstream comparisons.
        return new RenderTiming(DisplayRefreshHz: hz, FrameBudget: TimeSpan.FromTicks(TimeSpan.TicksPerSecond / hz));
    }

    private static unsafe int ProbePrimaryRefreshHz(LoggerHandle log, int fallbackHz)
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
            return fallbackHz;
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
            return fallbackHz;
        }
        return hz;
    }
}
