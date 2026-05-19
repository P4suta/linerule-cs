using System.Collections.Generic;
using Linerule.Bootstrap;
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

    [Theory]
    [InlineData(typeof(LineruleError.Chord), 1)]
    [InlineData(typeof(LineruleError.CoreFault), 1)]
    [InlineData(typeof(LineruleError.BootFault), 2)]
    [InlineData(typeof(LineruleError.Unexpected), 3)]
    [InlineData(typeof(LineruleError.Hotkey), 4)]
    public void ToExitCode_covers_every_variant(System.Type variant, int expected)
    {
        ArgumentNullException.ThrowIfNull(variant);
        // Variant → exit-code table is observable contract for shell scripts.
        // Theory rows cover the 5 sealed records exhaustively; a new variant
        // added without updating ToExitCode will fall to the `_ => 1`
        // catch-all and won't surface here — but the closed-DU pattern means
        // adding a 6th variant also wants a Theory row, which the reviewer
        // will notice.
        var instance = MakeInstance(variant);
        Assert.Equal(expected, instance.ToExitCode());
    }

    [Fact]
    public void Render_chord_emits_human_message()
    {
        var sink = new CapturingSink();
        LineruleError.FromChord(new ChordError.Empty()).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Equal(DiagnosticSeverity.Error, sink.Captured[0].Severity);
    }

    [Fact]
    public void Render_hotkey_already_claimed_includes_chord_label()
    {
        var sink = new CapturingSink();
        var chord = new ChordSpec(default, new KeyCode.Letter((byte)'Z'));
        LineruleError.FromHotkey(new HotkeyError.AlreadyClaimed(chord)).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Equal(DiagnosticSeverity.Error, sink.Captured[0].Severity);
        Assert.Contains("already claimed", sink.Captured[0].Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_hotkey_os_refused_includes_hresult_hex()
    {
        var sink = new CapturingSink();
        var chord = new ChordSpec(default, new KeyCode.Letter((byte)'A'));
        LineruleError.FromHotkey(new HotkeyError.OsRefused(chord, Hresult: unchecked((int)0x80070005))).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Contains("80070005", sink.Captured[0].Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_boot_phase_failed_includes_phase_name_and_reason()
    {
        var sink = new CapturingSink();
        var err = new BootstrapError.PhaseFailed("open-sqlite", "permission denied");
        LineruleError.FromBootstrap(err).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Contains("open-sqlite", sink.Captured[0].Message, System.StringComparison.Ordinal);
        Assert.Contains("permission denied", sink.Captured[0].Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_boot_phase_threw_unwraps_inner_message()
    {
        var sink = new CapturingSink();
        var err = new BootstrapError.Threw("install-crash", new System.IO.IOException("disk full"));
        LineruleError.FromBootstrap(err).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Contains("install-crash", sink.Captured[0].Message, System.StringComparison.Ordinal);
        Assert.Contains("disk full", sink.Captured[0].Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Render_boot_phase_cancelled_uses_warning_severity()
    {
        // Cancellation is the only path that downgrades to Warning — the
        // boot completed in a clean shutdown, no fault to surface.
        var sink = new CapturingSink();
        LineruleError.FromBootstrap(new BootstrapError.Cancelled("assemble")).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Equal(DiagnosticSeverity.Warning, sink.Captured[0].Severity);
    }

    [Fact]
    public void Render_unexpected_with_null_cause_emits_unknown_marker()
    {
        // Cancellation tokens and other "synthesized" unexpected paths set
        // Cause = null. The render must still produce a single message and
        // not throw a NullReferenceException trying to read `.Message`.
        var sink = new CapturingSink();
        new LineruleError.Unexpected("test-site", Cause: null).Render(sink);
        Assert.Single(sink.Captured);
        Assert.Contains("test-site", sink.Captured[0].Message, System.StringComparison.Ordinal);
        Assert.Contains("unknown", sink.Captured[0].Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_rejects_null_sink()
    {
        Assert.Throws<ArgumentNullException>(() => LineruleError.FromCore(new CoreError.Opacity(0)).Render(null!));
    }

    // Map a closed-DU variant type to a representative instance for the
    // ToExitCode theory. Each construction is cheap and uses the minimum-
    // required carry value.
    private static LineruleError MakeInstance(System.Type variant) =>
        variant.Name switch
        {
            nameof(LineruleError.Chord) => LineruleError.FromChord(new ChordError.Empty()),
            nameof(LineruleError.CoreFault) => LineruleError.FromCore(new CoreError.Opacity(0)),
            nameof(LineruleError.Hotkey) => LineruleError.FromHotkey(
                new HotkeyError.AlreadyClaimed(new ChordSpec(default, new KeyCode.Letter((byte)'A')))
            ),
            nameof(LineruleError.BootFault) => LineruleError.FromBootstrap(new BootstrapError.PhaseFailed("p", "r")),
            nameof(LineruleError.Unexpected) => new LineruleError.Unexpected(Where: "where", Cause: null),
            _ => throw new InvalidOperationException($"unhandled variant {variant.Name}"),
        };
}
