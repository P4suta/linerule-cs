using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Capability-token output of <see cref="BootDag.InitLogger"/>: the logger
/// is live and the sink is wired into its pipeline.
/// </summary>
public sealed record LoggerSeed(SqliteSeed Sqlite, LoggerRoot Logger);
