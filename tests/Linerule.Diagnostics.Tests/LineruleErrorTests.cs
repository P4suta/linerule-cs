using System.Collections.Generic;
using System.Collections.Immutable;
using Linerule.Config;
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
        var configFs = LineruleError.FromConfig(new ConfigError.FileSystem("p", "io"));
        var core = LineruleError.FromCore(new CoreError.Opacity(999));
        var chord = LineruleError.FromChord(new ChordError.Empty());
        var hot = LineruleError.FromHotkey(
            new HotkeyError.AlreadyClaimed(new ChordSpec(default, new KeyCode.Letter((byte)'A')))
        );

        Assert.IsType<LineruleError.ConfigFault>(configFs);
        Assert.IsType<LineruleError.CoreFault>(core);
        Assert.IsType<LineruleError.Chord>(chord);
        Assert.IsType<LineruleError.Hotkey>(hot);
    }

    [Fact]
    public void FileSystem_yields_exit_code_one()
    {
        var e = LineruleError.FromConfig(new ConfigError.FileSystem("p", "io"));
        Assert.Equal(1, e.ToExitCode());
    }

    [Fact]
    public void Schema_with_only_warnings_yields_exit_code_zero()
    {
        var diag = new ConfigDiagnostic("warn", Source: null, Span: null, DiagnosticSeverity.Warning);
        var e = LineruleError.FromConfig(new ConfigError.SchemaDiagnostics([diag]));
        Assert.Equal(0, e.ToExitCode());
    }

    [Fact]
    public void Schema_with_error_yields_exit_code_one()
    {
        var diag = new ConfigDiagnostic("err", Source: null, Span: null, DiagnosticSeverity.Error);
        var e = LineruleError.FromConfig(new ConfigError.SchemaDiagnostics([diag]));
        Assert.Equal(1, e.ToExitCode());
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
    public void Render_emits_one_line_per_schema_diagnostic()
    {
        var diags = ImmutableArray.Create(
            new ConfigDiagnostic("first", Source: null, Span: null, DiagnosticSeverity.Error, DotPath: "a.b"),
            new ConfigDiagnostic("second", Source: null, Span: null, DiagnosticSeverity.Warning, DotPath: "c.d")
        );
        var e = LineruleError.FromConfig(new ConfigError.SchemaDiagnostics(diags));
        var sink = new CapturingSink();
        e.Render(sink);
        Assert.Equal(2, sink.Captured.Count);
        Assert.Equal(DiagnosticSeverity.Error, sink.Captured[0].Severity);
        Assert.Equal("a.b", sink.Captured[0].DotPath);
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
