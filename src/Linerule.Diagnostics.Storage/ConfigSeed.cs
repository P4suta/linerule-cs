using System.Collections.Immutable;
using Linerule.Config;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Capability-token output of <see cref="BootDag.LoadConfig"/>: the resolved
/// <see cref="UserConfig"/> plus any non-fatal diagnostics the validator
/// surfaced as warnings.
/// </summary>
public sealed record ConfigSeed(CrashSeed Crash, UserConfig Config, ImmutableArray<ConfigDiagnostic> Warnings);
