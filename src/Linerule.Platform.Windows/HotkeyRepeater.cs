using System;
using Linerule.Config;
using Linerule.Core;
using Linerule.Input;
using Linerule.Platform;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Win32;
using Microsoft.UI.Dispatching;
using Windows.Win32;

namespace Linerule.Platform.Windows;

/// <summary>
/// Win32 adapter for the pure <see cref="HoldFsm"/> finite-state machine in
/// <c>Linerule.Input</c>. Bridges three concerns the FSM can't see:
///
/// <list type="bullet">
///   <item>
///     <b>WM_HOTKEY ingress</b> — <c>HotkeyHost.OnHotkeyFired</c> delivers the
///     OS event; we package it as <see cref="HoldInput.Fired"/> and feed it
///     to <see cref="HoldFsm.Step"/>.
///   </item>
///   <item>
///     <b>Live key-down probe</b> — <c>RegisterHotKey</c> with
///     <c>MOD_NOREPEAT</c> fires once per press, so we poll
///     <c>GetAsyncKeyState</c>'s high bit for the live "key is down right now"
///     signal and build <see cref="HoldInput.Tick"/>.
///   </item>
///   <item>
///     <b>Effect dispatch</b> — the FSM returns <see cref="HoldEffect"/>s
///     (Emit / Schedule / Stop); this adapter applies them to
///     <see cref="HotkeyHost.Enqueue"/> and the dispatcher-queue timer.
///   </item>
/// </list>
///
/// <para>
/// Threading: the poll timer runs on the UI thread via
/// <see cref="DispatcherQueueTimer"/>; <c>GetAsyncKeyState</c> reads an
/// OS-owned table and is safe from any thread.
/// </para>
/// </summary>
internal sealed partial class HotkeyRepeater : IDisposable
{
    private readonly HotkeyHost _host;
    private readonly DispatcherQueueTimer _timer;
    private readonly ISaturationOracle _oracle;
    private readonly RepeatConfig _config;
    private readonly LoggerHandle _log;

    private HoldState _state = HoldState.Idle.Instance;
    private bool _disposed;

    public HotkeyRepeater(
        DispatcherQueue queue,
        HotkeyHost host,
        ISaturationOracle oracle,
        RepeatConfig repeatCfg,
        LoggerHandle log
    )
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentNullException.ThrowIfNull(repeatCfg);

        _host = host;
        _oracle = oracle;
        _config = repeatCfg;
        _log = log;
        _timer = queue.CreateTimer();
        _timer.Tick += OnTimerTick;
        _host.OnHotkeyFired = OnHotkeyFired;
        _log.Debug("HotkeyRepeater initialized");
    }

    private void OnHotkeyFired(ChordSpec chord, OverlayAction action)
    {
        if (_disposed)
        {
            return;
        }
        var input = new HoldInput.Fired(chord, action, Environment.TickCount64);
        var (next, effects) = HoldFsm.Step(_state, input, _config);
        _state = next;
        ApplyEffects(effects);
    }

    private void OnTimerTick(object? sender, object args)
    {
        if (_disposed)
        {
            _timer.Stop();
            return;
        }
        var chord = ActiveChord(_state);
        if (chord is null)
        {
            _timer.Stop();
            return;
        }
        var input = new HoldInput.Tick(NowMs: Environment.TickCount64, StillHeld: IsChordHeld(chord), Oracle: _oracle);
        var (next, effects) = HoldFsm.Step(_state, input, _config);
        _state = next;
        ApplyEffects(effects);
    }

    private void ApplyEffects(IReadOnlyList<HoldEffect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case HoldEffect.Enqueue emit:
                    _host.Enqueue(emit.Action);
                    break;
                case HoldEffect.Schedule sched:
                    _timer.Interval = sched.Next;
                    _timer.Start();
                    break;
                case HoldEffect.Stop:
                    _timer.Stop();
                    break;
                default:
                    // Exhaustive closed coproduct — any new variant becomes a
                    // compile error in HoldEffect.cs; this arm preserves
                    // forward-compat at the adapter boundary.
                    break;
            }
        }
    }

    private static ChordSpec? ActiveChord(HoldState state) =>
        state switch
        {
            HoldState.Repeating r => r.Chord,
            HoldState.AwaitingRelease a => a.Chord,
            _ => null,
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
    /// <c>GetAsyncKeyState</c>'s high bit (0x8000) is set while the key is
    /// currently down. The low bit is "pressed since last call" —
    /// deliberately not used here; we want the live state.
    /// </summary>
    private static bool IsVkDown(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0;

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
        _state = HoldState.Idle.Instance;
        _log.Debug("HotkeyRepeater disposed");
    }
}
