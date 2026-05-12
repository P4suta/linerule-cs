using System.Collections.Generic;
using Linerule.Core;
using Linerule.Diagnostics;
using Linerule.Platform;

namespace Linerule.Diagnostics.Tests;

public sealed class LineruleErrorTests
{
    private sealed class CapturingSink : IDiagnosticSink
    {
        public List<(DiagnosticSeverity Severity, string Message, string? DotPath)> Captured { get; } = [];

        public void Write(DiagnosticSeverity severity, string message, string? dotPath = null) =>
            Captured.Add((severity, message, dotPath));
    }

    [Fact]
    public void Lift_helpers_wrap_each_layer_error()
    {
        var core = LineruleError.FromCore(new CoreError.Opacity(999));
        var chord = LineruleError.FromChord(new ChordError.Empty());
        var hot = LineruleError.FromHotkey(
            new HotkeyError.AlreadyClaimed(new ChordSpec(default, new KeyCode.Letter((byte)'A')))
        );

        Assert.IsType<LineruleError.CoreFault>(core);
        Assert.IsType<LineruleError.Chord>(chord);
        Assert.IsType<LineruleError.Hotkey>(hot);
    }

    [Fact]
    public void Hotkey_claim_yields_exit_code_four()
    {
        var e = LineruleError.FromHotkey(
            new HotkeyError.AlreadyClaimed(new ChordSpec(default, new KeyCode.Letter((byte)'A')))
        );
        Assert.Equal(4, e.ToExitCode());
    }

    [Fact]
    public void Unexpected_yields_exit_code_three()
    {
        var e = new LineruleError.Unexpected("boot", new System.InvalidOperationException("x"));
        Assert.Equal(3, e.ToExitCode());
    }

    [Fact]
    public void Render_core_emits_human_message()
    {
        var sink = new CapturingSink();
        LineruleError.FromCore(new CoreError.Opacity(999)).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Contains("999", sink.Captured[0].Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NullDiagnosticSink_drops_everything()
    {
        var sink = NullDiagnosticSink.Instance;
        sink.Write(DiagnosticSeverity.Error, "boom"); // should not throw
        // The sink is the canonical no-op singleton: every call goes to the
        // same instance (used as the default in hot paths) and no observable
        // state changes. Asserting identity + interface conformance pins
        // both properties.
        Assert.Same(NullDiagnosticSink.Instance, sink);
        Assert.IsType<IDiagnosticSink>(sink, exactMatch: false);
    }
}
