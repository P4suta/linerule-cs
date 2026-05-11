using Linerule.Core;

namespace Linerule.Input;

/// <summary>
/// Side effects the pure <see cref="TickPipeline.Step"/> wants the adapter
/// to apply, in order. Returned as a list (Elm-style <c>(model, cmd)</c>)
/// so the adapter is a thin pattern-match — no decision logic in the
/// platform layer.
/// </summary>
public abstract record TickEffect
{
    private protected TickEffect() { }

    /// <summary>Quit was observed in the hotkey queue: leave the event loop.</summary>
    public sealed record Quit : TickEffect
    {
        public static Quit Instance { get; } = new();
    }

    /// <summary>Draw an overlay frame for the supplied state at <paramref name="Cursor"/>.</summary>
    public sealed record DrawOverlay(Mode Mode, Point<Logical> Cursor, OverlayConfig Config) : TickEffect;

    /// <summary>Clear the overlay (visible=false or mode=Off).</summary>
    public sealed record ClearOverlay : TickEffect
    {
        public static ClearOverlay Instance { get; } = new();
    }

    /// <summary>Refresh the HUD with the current state and telemetry snapshot.</summary>
    public sealed record RefreshHud(State State) : TickEffect;

    /// <summary>Recompute and push the HUD opacity for the latest cursor position.</summary>
    public sealed record SetHudOpacity(State State, Point<Logical> Cursor) : TickEffect;

    /// <summary>Structured log of a successful state delta — adapter routes to its logger handle.</summary>
    public sealed record LogStateChanged(OverlayAction Action, Mode Mode, bool Visible) : TickEffect;
}
