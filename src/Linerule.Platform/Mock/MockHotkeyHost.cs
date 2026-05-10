using System.Threading.Channels;
using Linerule.Core;

namespace Linerule.Platform.Mock;

/// <summary>Test double: register-then-fire scripted hotkey events.</summary>
public sealed class MockHotkeyHost : IHotkeyHost
{
    private readonly Channel<OverlayAction> _channel = Channel.CreateUnbounded<OverlayAction>();
    private readonly Dictionary<ChordSpec, OverlayAction> _bindings = new();

    public Result<Unit, HotkeyError> Register(ChordSpec chord, OverlayAction action)
    {
        _bindings[chord] = action;
        return Result.Ok<Unit, HotkeyError>(Unit.Value);
    }

    /// <summary>Tests call this to simulate a hotkey press. Method, not event — the
    /// test drives input rather than reacting to it.</summary>
    public ValueTask SendAsync(ChordSpec chord, CancellationToken cancellationToken = default)
    {
        if (!_bindings.TryGetValue(chord, out var action))
        {
            return ValueTask.CompletedTask;
        }

        return _channel.Writer.WriteAsync(action, cancellationToken);
    }

    public IAsyncEnumerable<OverlayAction> Subscribe(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
