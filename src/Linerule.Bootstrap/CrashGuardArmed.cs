namespace Linerule.Bootstrap;

/// <summary>The unhandled-exception filter / crash-dump emitter has been installed.</summary>
public sealed record CrashGuardArmed(LoggerLive Log, System.IDisposable Registration) : CapabilityToken;
