namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Capability-token output of <see cref="BootDag.OpenSqlite"/>: the event
/// sink is opened, the WAL is ready, and <see cref="RunMetadata"/> rows have
/// been written. Lifetime is tied to <see cref="AppContext.DisposeAsync"/>.
/// </summary>
public sealed record SqliteSeed(SqliteEventSink Sink, string DbPath);
