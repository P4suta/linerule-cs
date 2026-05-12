using System.Diagnostics.CodeAnalysis;
using Linerule.Core;

namespace Linerule.Config.Tests;

/// <summary>
/// ADR-0010 test-side suppression boundary. These tests exist to verify
/// the Tomlyn-backed <see cref="ConfigLoader.ParseString(string, string?)"/>
/// integration — calling the annotated member is the point. A single
/// shim with <c>[UnconditionalSuppressMessage]</c> keeps the suppression
/// scoped: any NEW reflection introduced in this test assembly outside
/// of <c>ConfigLoader.ParseString</c> will still trip IL2026/IL3050.
/// </summary>
internal static class AotTestBoundary
{
    private const string Justification =
        "ADR-0010: tests for Linerule.Config legitimately exercise Tomlyn reflection. "
        + "Tests are never AOT-published; per-site suppression keeps the gate elsewhere.";

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode method called",
        Justification = Justification
    )]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode method called", Justification = Justification)]
    public static Result<UserConfig, ConfigError> ParseString(string text, string? sourcePath = null) =>
        ConfigLoader.ParseString(text, sourcePath);
}
