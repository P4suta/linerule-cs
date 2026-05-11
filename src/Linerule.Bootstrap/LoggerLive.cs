namespace Linerule.Bootstrap;

/// <summary>The diagnostic <c>LoggerRoot</c> has been constructed and seeded with the sink.</summary>
public sealed record LoggerLive(SqliteOpened Sink, System.IAsyncDisposable Root) : CapabilityToken;
