using Linerule.Bootstrap;
using Linerule.Config;
using Linerule.Core;
using Linerule.Platform;

namespace Linerule.Diagnostics;

/// <summary>
/// Top-level closed coproduct that wraps every per-layer error type so the
/// composition root and CLI subcommands fold exits through a single
/// <see langword="switch"/>. Layers keep their narrow <see cref="ConfigError"/>,
/// <see cref="CoreError"/>, <see cref="ChordError"/>, <see cref="HotkeyError"/>;
/// the lift to <see cref="LineruleError"/> happens at the boundary via
/// <c>Result.MapErr</c> (or directly via the static factory below).
///
/// <para>
/// Numeric <see cref="ExitCode"/>s are defined by <see cref="ExitCode"/>; the
/// mapping from each variant is intentionally small and explicit so a script
/// can branch on the integer without parsing the human render.
/// </para>
/// <para>
/// Variant names are suffixed with <c>Fault</c> / <c>Unexpected</c> to avoid
/// CA1724 collisions with the like-named <c>Linerule.Config</c>,
/// <c>Linerule.Core</c>, <c>Linerule.Bootstrap</c> namespaces and the BCL's
/// <c>System.Runtime</c> / <c>System.Deployment.Internal</c>; the wrapped
/// <c>Inner</c> field keeps the same layer-specific shape.
/// </para>
/// </summary>
public abstract record LineruleError
{
    private protected LineruleError() { }

    /// <summary>A config-loader failure (filesystem or schema diagnostics).</summary>
    public sealed record ConfigFault(ConfigError Inner) : LineruleError;

    /// <summary>A <c>Linerule.Core</c> smart-constructor rejection (out-of-range Opacity / Thickness, …).</summary>
    public sealed record CoreFault(CoreError Inner) : LineruleError;

    /// <summary>A chord-parsing failure raised by <c>ChordParser</c>.</summary>
    public sealed record Chord(ChordError Inner) : LineruleError;

    /// <summary>OS / platform hotkey-registration failure.</summary>
    public sealed record Hotkey(HotkeyError Inner) : LineruleError;

    /// <summary>A <see cref="Linerule.Bootstrap.Phase{TIn, TOut}"/> failed during composition root assembly.</summary>
    public sealed record BootFault(BootstrapError Inner) : LineruleError;

    /// <summary>
    /// Catch-all for runtime failures that don't have a dedicated layer
    /// error (e.g. a Win32 / WinAppSDK callback that threw). Carries the
    /// originating site label and the captured exception (may be null when
    /// the failure was synthesised, e.g. a cancellation).
    /// </summary>
    public sealed record Unexpected(string Where, Exception? Cause) : LineruleError;

    /// <summary>
    /// Compute the integer process exit code. Stable so external scripts
    /// can branch on it; matches the historical <c>DiagnosticPrinter</c>
    /// returns where applicable.
    /// </summary>
    public int ToExitCode() =>
        this switch
        {
            ConfigFault { Inner: ConfigError.SchemaDiagnostics sd }
                when sd.Items.All(d => d.Severity < DiagnosticSeverity.Error) => 0,
            ConfigFault => 1,
            Chord => 1,
            Hotkey => 4,
            CoreFault => 1,
            BootFault => 2,
            Unexpected => 3,
            _ => 1,
        };

    /// <summary>Render a human-readable summary through <paramref name="sink"/>.</summary>
    public void Render(IDiagnosticSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        switch (this)
        {
            case ConfigFault { Inner: ConfigError.FileSystem io }:
                sink.Write(DiagnosticSeverity.Error, $"cannot read {io.Path}: {io.Reason}");
                break;
            case ConfigFault { Inner: ConfigError.SchemaDiagnostics sd }:
                foreach (var d in sd.Items)
                {
                    sink.Write(d.Severity, d.Message, d.DotPath);
                }
                break;
            case CoreFault { Inner: var cause }:
                sink.Write(DiagnosticSeverity.Error, cause.ToHumanString());
                break;
            case Chord { Inner: var cause }:
                sink.Write(DiagnosticSeverity.Error, cause.ToHumanString());
                break;
            case Hotkey { Inner: HotkeyError.AlreadyClaimed claim }:
                sink.Write(DiagnosticSeverity.Error, $"hotkey already claimed: {claim.Chord}");
                break;
            case Hotkey { Inner: HotkeyError.OsRefused os }:
                sink.Write(DiagnosticSeverity.Error, $"OS refused hotkey registration ({os.Hresult:X8}): {os.Chord}");
                break;
            case BootFault { Inner: BootstrapError.PhaseFailed pf }:
                sink.Write(DiagnosticSeverity.Error, $"bootstrap phase `{pf.PhaseName}` failed: {pf.Reason}");
                break;
            case BootFault { Inner: BootstrapError.Threw t }:
                sink.Write(DiagnosticSeverity.Error, $"bootstrap phase `{t.PhaseName}` threw: {t.Cause.Message}");
                break;
            case BootFault { Inner: BootstrapError.Cancelled c }:
                sink.Write(DiagnosticSeverity.Warning, $"bootstrap phase `{c.PhaseName}` cancelled");
                break;
            case Unexpected r:
                sink.Write(DiagnosticSeverity.Error, $"{r.Where}: {r.Cause?.Message ?? "<unknown>"}");
                break;
            default:
                sink.Write(DiagnosticSeverity.Error, "unknown linerule error");
                break;
        }
    }

    /// <summary>Convenience lift: wrap a <see cref="ConfigError"/>.</summary>
    public static LineruleError FromConfig(ConfigError inner) => new ConfigFault(inner);

    /// <summary>Convenience lift: wrap a <see cref="CoreError"/>.</summary>
    public static LineruleError FromCore(CoreError inner) => new CoreFault(inner);

    /// <summary>Convenience lift: wrap a <see cref="ChordError"/>.</summary>
    public static LineruleError FromChord(ChordError inner) => new Chord(inner);

    /// <summary>Convenience lift: wrap a <see cref="HotkeyError"/>.</summary>
    public static LineruleError FromHotkey(HotkeyError inner) => new Hotkey(inner);

    /// <summary>Convenience lift: wrap a <see cref="BootstrapError"/>.</summary>
    public static LineruleError FromBootstrap(BootstrapError inner) => new BootFault(inner);
}
