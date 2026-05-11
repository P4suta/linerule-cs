using System;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Microsoft.UI.Dispatching;
using Windows.Win32;

namespace Linerule.Platform.Windows;

/// <summary>
/// Long-press handler for the hotkey layer. Three hold patterns,
/// dispatched on the bound <see cref="OverlayAction"/>:
///
/// <list type="bullet">
///   <item>
///     <b>Accelerating repeat</b> — <see cref="OverlayAction.BumpThickness"/>,
///     <see cref="OverlayAction.BumpOpacity"/>. While held, fire a
///     ±1-unit step. Cadence ramps 20→160 Hz across the first 3 s so the
///     start of a press is "micro-adjust" and a sustained hold "sweeps"
///     the full range (Thickness 1..1024, Opacity 1..255). User-tuned
///     2026-05-11 to feel <i>「無段階」</i>: the held train is small enough
///     that each step is perceptually invisible, only the destination
///     moves.
///   </item>
///   <item>
///     <b>Slow repeat</b> — <see cref="OverlayAction.CycleMode"/>. The
///     mode space is tiny (3 values) so the accelerating curve would just
///     loop. Flat 400 ms cadence lets the user release on a specific mode.
///   </item>
///   <item>
///     <b>Long-press undo</b> — <see cref="OverlayAction.ToggleVisible"/>.
///     Tap (release before <see cref="LongPressThresholdMs"/>) lets the
///     toggle stick — the persistent on/off behavior the user expects of
///     a checkbox-style chord. Hold past the threshold re-fires the same
///     toggle on release, returning the state to whatever it was before
///     the press. Net effect: "press-and-hold to peek through the
///     overlay, release to restore" (user feedback 2026-05-11
///     "長押しから手を離したら普通に一時無効化をやめる").
///   </item>
/// </list>
///
/// <para>
/// <b>Why a poller at all</b>: <c>RegisterHotKey</c> is set with
/// <c>MOD_NOREPEAT</c> so the OS fires <c>WM_HOTKEY</c> exactly once per
/// press. Without polling we have no signal for either "still held" or
/// "released". <see cref="PInvoke.GetAsyncKeyState"/>'s high-bit mask is
/// the live "key is down right now" signal — the only Win32 primitive
/// that gives both edges on a hotkey-grabbed key.
/// </para>
///
/// <para>
/// <b>Threading</b>: polling runs on the
/// <see cref="DispatcherQueueTimer"/>'s queue thread, which is the UI
/// thread. <see cref="PInvoke.GetAsyncKeyState"/> is safe to call from
/// any thread (read-only against an OS-owned key-state table).
/// </para>
/// </summary>
internal sealed partial class HotkeyRepeater : IDisposable
{
    private static readonly LoggerHandle Log = Logger.For(Subsystems.Hotkey);

    private readonly int _initialDelayMs;
    private readonly int _longPressThresholdMs;
    private readonly int _slowRepeatIntervalMs;
    private readonly int _releasePollIntervalMs;

    private readonly HotkeyHost _host;
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<OverlayAction, bool> _wouldAffectState;
    private ChordSpec? _heldChord;
    private HoldKind _holdKind;
    private RepeatCadence _cadence;
    private OverlayAction? _repeatAction; // Repeat mode: step action emitted each tick.
    private OverlayAction? _undoAction; // LongPressUndo mode: emitted on release IFF held ≥ threshold.
    private long _holdStartMs;
    private bool _disposed;

    private enum HoldKind
    {
        None,

        /// <summary>Fire the bound action repeatedly while the chord is held.</summary>
        Repeat,

        /// <summary>
        /// Fire <see cref="_undoAction"/> exactly once when the chord is
        /// released, but ONLY if the press has lasted at least
        /// <see cref="LongPressThresholdMs"/>. A tap-and-release is left
        /// alone — the WM_HOTKEY firing remains the only state change.
        /// </summary>
        LongPressUndo,
    }

    /// <summary>
    /// Cadence shape for <see cref="HoldKind.Repeat"/>.
    /// <see cref="Accelerating"/> is the Bump variant — 20→160 Hz over
    /// 3 s, designed for value sweeps. <see cref="Slow"/> is flat 400 ms,
    /// for small enums where a fast repeat would just loop.
    /// </summary>
    private enum RepeatCadence
    {
        Accelerating,
        Slow,
    }

    public HotkeyRepeater(
        DispatcherQueue queue,
        HotkeyHost host,
        Func<OverlayAction, bool> wouldAffectState,
        RepeatConfig repeatCfg
    )
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(wouldAffectState);
        ArgumentNullException.ThrowIfNull(repeatCfg);
        _initialDelayMs = repeatCfg.InitialDelayMs;
        _longPressThresholdMs = repeatCfg.LongPressThresholdMs;
        _slowRepeatIntervalMs = repeatCfg.SlowRepeatIntervalMs;
        _releasePollIntervalMs = repeatCfg.ReleasePollMs;
        _host = host;
        _wouldAffectState = wouldAffectState;
        _timer = queue.CreateTimer();
        _timer.Tick += OnTimerTick;
        _host.OnHotkeyFired = OnHotkeyFired;
        // Touch the static Log field NOW so the type initializer fires
        // while Logger is still healthy. Without this, the only access
        // to Log is in Dispose() — by which point Logger has been torn
        // down, and the cctor throws TypeInitializationException
        // (verified 2026-05-11 in a real shutdown trace).
        Log.Debug("HotkeyRepeater initialized");
    }

    private void OnHotkeyFired(ChordSpec chord, OverlayAction action)
    {
        if (_disposed)
        {
            return;
        }
        switch (action)
        {
            case OverlayAction.BumpThickness t:
                // `Math.Sign(0)` is 0, which would seed a repeat that emits
                // `BumpThickness(0)` forever (a guaranteed no-op). The
                // saturation predicate would catch it on the first tick,
                // but bailing here is cheaper and removes a class of
                // silent-no-op surprises if a future binding sets Delta=0.
                var thicknessSign = Math.Sign(t.Delta);
                if (thicknessSign == 0)
                {
                    CancelHold();
                    break;
                }
                BeginRepeat(chord, new OverlayAction.BumpThickness(thicknessSign), RepeatCadence.Accelerating);
                break;
            case OverlayAction.BumpOpacity o:
                var opacitySign = Math.Sign(o.Delta);
                if (opacitySign == 0)
                {
                    CancelHold();
                    break;
                }
                BeginRepeat(chord, new OverlayAction.BumpOpacity(opacitySign), RepeatCadence.Accelerating);
                break;
            case OverlayAction.CycleMode:
                BeginRepeat(chord, OverlayAction.CycleMode.Instance, RepeatCadence.Slow);
                break;
            case OverlayAction.ToggleVisible:
                // Tap (released < threshold) → toggle sticks. Hold ≥
                // threshold → fire ToggleVisible again on release to
                // restore the prior state. The "undo" action is the
                // SAME ToggleVisible — a symmetric flip reverses itself.
                BeginLongPressUndo(chord, OverlayAction.ToggleVisible.Instance);
                break;
            default:
                // Quit / etc. — not a hold target. Cancel any in-progress
                // hold (defensive; one chord drives one action so this
                // rarely fires).
                CancelHold();
                break;
        }
    }

    private void BeginRepeat(ChordSpec chord, OverlayAction stepAction, RepeatCadence cadence)
    {
        _heldChord = chord;
        _holdKind = HoldKind.Repeat;
        _cadence = cadence;
        _repeatAction = stepAction;
        _undoAction = null;
        _holdStartMs = Environment.TickCount64;
        _timer.Interval = TimeSpan.FromMilliseconds(_initialDelayMs);
        _timer.Start();
    }

    private void BeginLongPressUndo(ChordSpec chord, OverlayAction undoAction)
    {
        _heldChord = chord;
        _holdKind = HoldKind.LongPressUndo;
        _repeatAction = null;
        _undoAction = undoAction;
        _holdStartMs = Environment.TickCount64;
        // Steady poll for release detection — no acceleration needed.
        _timer.Interval = TimeSpan.FromMilliseconds(_releasePollIntervalMs);
        _timer.Start();
    }

    private void OnTimerTick(object? sender, object args)
    {
        if (_disposed || _heldChord is null || _holdKind == HoldKind.None)
        {
            _timer.Stop();
            return;
        }
        if (!IsChordHeld(_heldChord))
        {
            HandleRelease();
            return;
        }
        switch (_holdKind)
        {
            case HoldKind.Repeat:
                var repeatHeldMs = Environment.TickCount64 - _holdStartMs;
                var (repeatInterval, repeatMagnitude) = ComputeNextStep(repeatHeldMs, _cadence);
                if (_repeatAction is not null)
                {
                    var scaled = WithStepMagnitude(_repeatAction, repeatMagnitude);
                    // If the action would be a no-op given current state
                    // (saturated at MAX/MIN, or the overlay is in Off
                    // mode), stop polling — there's nothing useful to
                    // emit and a long hold under those conditions used
                    // to degrade HUD perf (2026-05-11 user report).
                    if (!_wouldAffectState(scaled))
                    {
                        CancelHold();
                        return;
                    }
                    _host.Enqueue(scaled);
                }
                _timer.Interval = repeatInterval;
                break;
            case HoldKind.LongPressUndo:
                // Keep polling at the same fixed cadence; nothing fires
                // while held, only on release.
                _timer.Interval = TimeSpan.FromMilliseconds(_releasePollIntervalMs);
                break;
            case HoldKind.None:
                _timer.Stop();
                break;
            default:
                // Unknown enum value — treat the same as None and bail.
                _timer.Stop();
                break;
        }
    }

    private void HandleRelease()
    {
        if (_holdKind == HoldKind.LongPressUndo && _undoAction is not null)
        {
            var heldMs = Environment.TickCount64 - _holdStartMs;
            if (heldMs >= _longPressThresholdMs)
            {
                _host.Enqueue(_undoAction);
            }
        }
        CancelHold();
    }

    /// <summary>
    /// Map hold-time-since-press → (next repeat interval, step
    /// magnitude). Rate is capped at vsync (~60 Hz / 16 ms) because
    /// Composition tree mutations faster than vsync (a) can't be
    /// observed and (b) appear to crash the GPU pipeline under sustained
    /// load (2026-05-11: hard process exit during Thicker long-press at
    /// ≥ 80 Hz emission, no managed stack). Step magnitude stays at 1
    /// for the first 3 s of hold so micro-adjust feels stepless; after
    /// that it bumps to 4 to let a sustained press sweep the full range
    /// (Thickness 1..2048) in roughly 10 s.
    /// <see cref="RepeatCadence.Slow"/> is a flat 400 ms with step 1 —
    /// meant for small finite enums like Mode where rapid cycling would
    /// just loop.
    /// </summary>
    private (TimeSpan Interval, int StepMagnitude) ComputeNextStep(long heldMs, RepeatCadence cadence) =>
        cadence == RepeatCadence.Slow
            ? (TimeSpan.FromMilliseconds(_slowRepeatIntervalMs), 1)
            : heldMs switch
            {
                < 1000 => (TimeSpan.FromMilliseconds(50), 1), // 20 Hz × 1 — micro
                < 2000 => (TimeSpan.FromMilliseconds(25), 1), // 40 Hz × 1
                < 3000 => (TimeSpan.FromMilliseconds(16), 1), // 60 Hz × 1 — smooth vsync cap
                _ => (TimeSpan.FromMilliseconds(16), 4), // 60 Hz × 4 — sweep phase, ≈240 unit/s
            };

    /// <summary>
    /// Scale a bump action's delta by the current step magnitude. Only
    /// the magnitude is varied; the sign carries through from the
    /// chord-bound direction (Thicker = +, Thinner = −).
    /// </summary>
    private static OverlayAction WithStepMagnitude(OverlayAction unitStep, int magnitude) =>
        unitStep switch
        {
            OverlayAction.BumpThickness t => new OverlayAction.BumpThickness(t.Delta * magnitude),
            OverlayAction.BumpOpacity o => new OverlayAction.BumpOpacity(o.Delta * magnitude),
            _ => unitStep,
        };

    private static bool IsChordHeld(ChordSpec chord)
    {
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12; // alt
        const int VK_SHIFT = 0x10;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;
        var vk = (int)VirtualKey.FromKeyCode(chord.Key);
        return IsVkDown(vk)
            && (!chord.Modifiers.Ctrl || IsVkDown(VK_CONTROL))
            && (!chord.Modifiers.Alt || IsVkDown(VK_MENU))
            && (!chord.Modifiers.Shift || IsVkDown(VK_SHIFT))
            && (!chord.Modifiers.Meta || IsVkDown(VK_LWIN) || IsVkDown(VK_RWIN));
    }

    /// <summary>
    /// <c>GetAsyncKeyState</c>'s high bit (0x8000) is set while the key
    /// is currently down. The low bit is "pressed since last call" —
    /// deliberately not used here; we want the live state.
    /// </summary>
    private static bool IsVkDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void CancelHold()
    {
        _heldChord = null;
        _holdKind = HoldKind.None;
        _repeatAction = null;
        _undoAction = null;
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _host.OnHotkeyFired = null;
        Log.Debug("HotkeyRepeater disposed");
    }
}
