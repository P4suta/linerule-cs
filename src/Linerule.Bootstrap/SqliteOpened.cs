namespace Linerule.Bootstrap;

/// <summary>The persistent event-sink has been opened and the WAL is ready.</summary>
public sealed record SqliteOpened(string DbPath, System.IAsyncDisposable Sink) : CapabilityToken;
