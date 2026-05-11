using System;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Capability-token output of <see cref="BootDag.InstallCrash"/>: the
/// unhandled-exception filter / crash-dump emitter is armed. Disposing the
/// returned registration removes the handler.
/// </summary>
public sealed record CrashSeed(LoggerSeed Logger, IDisposable Registration);
