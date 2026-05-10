using Linerule.Core;

namespace Linerule.Platform;

/// <summary>
/// Source of <see cref="OverlayAction"/>s driven by registered global hotkeys.
/// Backends listen on a hidden message-only window (Windows) or equivalent.
/// </summary>
public interface IHotkeyHost : IAsyncDisposable
{
    /// <summary>
    /// Register a binding from <paramref name="chord"/> to <paramref name="action"/>.
    /// On duplicate registration the most-recent wins (the previous is unregistered).
    /// </summary>
    /// <returns>Ok on success, error if the chord can't be claimed (already grabbed by another app).</returns>
    Result<Unit, HotkeyError> Register(ChordSpec chord, OverlayAction action);

    /// <summary>Stream of fired actions in the order they were posted by the OS.</summary>
    IAsyncEnumerable<OverlayAction> Subscribe(CancellationToken cancellationToken);
}
