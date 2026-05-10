using Linerule.Core;
using Linerule.Platform;

namespace Linerule.Platform.Tests;

public sealed class MockHotkeyHostTests
{
    [Fact]
    public async Task Send_routes_action_through_subscription()
    {
        await using var host = new global::Linerule.Platform.Mock.MockHotkeyHost();
        var chord = new ChordSpec(new Modifiers(true, true, false, false), new KeyCode.Letter((byte)'R'));
        host.Register(chord, OverlayAction.CycleMode.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var enumerator = host.Subscribe(cts.Token).GetAsyncEnumerator(cts.Token);

        await host.SendAsync(chord, cts.Token);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.IsType<OverlayAction.CycleMode>(enumerator.Current);
    }
}
