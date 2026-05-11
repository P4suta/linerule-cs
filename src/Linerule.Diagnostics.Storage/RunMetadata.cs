using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Per-process diagnostic identity row written once into <c>runs</c> when a
/// <see cref="SqliteEventSink"/> opens. Captures everything an analyst would
/// otherwise have to guess from log filenames (which binary, which host,
/// which invocation, what OS).
///
/// <para>
/// Constructed via <see cref="Capture(IReadOnlyList{string}, TimeProvider?)"/>
/// at logger init time; the timestamp is taken through
/// <see cref="TimeProvider"/> so tests can wind the clock without monkey
/// patching <c>DateTimeOffset.UtcNow</c> (banned by BannedSymbols.txt).
/// </para>
/// </summary>
public sealed record RunMetadata(
    Guid RunId,
    DateTimeOffset StartedAt,
    string Version,
    string BuildConfig,
    string Args,
    string Hostname,
    int Pid,
    string? OsVersion
)
{
    /// <summary>
    /// Capture the running process's identity.
    ///
    /// <para>
    /// <paramref name="argv"/> is the original command-line tail (without
    /// <c>argv[0]</c>); we join with spaces so the recorded value is
    /// shell-friendly to paste into a reproduction. Quoting fidelity is
    /// not preserved (an analyst will accept "argv0 --foo bar baz" as a
    /// breadcrumb, not a verbatim command).
    /// </para>
    ///
    /// <para>
    /// <paramref name="timeProvider"/> defaults to
    /// <see cref="TimeProvider.System"/>; tests pass a
    /// <c>FakeTimeProvider</c>.
    /// </para>
    /// </summary>
    public static RunMetadata Capture(IReadOnlyList<string> argv, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(argv);

        var time = timeProvider ?? TimeProvider.System;
        var asm = typeof(RunMetadata).GetTypeInfo().Assembly;

        return new RunMetadata(
            RunId: Guid.NewGuid(),
            StartedAt: time.GetUtcNow(),
            Version: ReadAssemblyVersion(asm),
            BuildConfig: ReadBuildConfig(),
            Args: string.Join(' ', argv),
            Hostname: Environment.MachineName,
            Pid: Environment.ProcessId,
            OsVersion: RuntimeInformation.OSDescription
        );
    }

    /// <summary>
    /// Prefer the SourceLink-stamped
    /// <see cref="AssemblyInformationalVersionAttribute"/> (which encodes
    /// the +sha suffix MinVer/Nerdbank.GitVersioning emit), falling back to
    /// the plain <see cref="AssemblyName.Version"/> if the metadata is
    /// absent (e.g. dotnet-test in a worktree without SourceLink).
    /// </summary>
    private static string ReadAssemblyVersion(Assembly asm)
    {
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (info is not null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
        {
            return info.InformationalVersion;
        }
        var name = asm.GetName().Version;
        return name?.ToString() ?? "0.0.0";
    }

    /// <summary>
    /// "Debug" vs "Release" — established by the <c>DEBUG</c> compilation
    /// symbol of THIS assembly. The CLI's build flavor is what matters for
    /// reproduction; that the storage assembly's build flavor matches is
    /// guaranteed by the workspace-wide <c>Configuration</c>.
    /// </summary>
    private static string ReadBuildConfig() =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif
}
