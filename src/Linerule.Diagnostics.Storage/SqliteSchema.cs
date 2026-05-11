using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Idempotent schema migrator. Every <see cref="SqliteCommand.CommandText"/>
/// assignment is wired to a <c>const string</c> field, so CA2100 (review
/// SQL for vulnerabilities) proves constancy at each callsite — no
/// <c>SuppressMessage</c> escape hatch required.
///
/// <para>
/// The sibling <c>schema.sql</c> file is kept as <b>documentation</b>:
/// sqlfluff / sqlite3 CLI / IDE SQL plugins can preview the canonical
/// shape, and the file remains the single source humans read. Drift
/// between the .sql file and the C# literals here is caught at test time
/// by <c>SchemaTests.Sql_file_matches_inline_statements</c>; the C# side
/// is the runtime authority.
/// </para>
///
/// <para>
/// All DDL is <c>CREATE … IF NOT EXISTS</c>, and the PRAGMAs are
/// connection-scoped (must be re-applied every open) but also idempotent —
/// so <see cref="Apply"/> is safe to call on every
/// <see cref="SqliteConnection.Open"/>.
/// </para>
/// </summary>
internal static class SqliteSchema
{
    private const string PragmaJournalMode = "PRAGMA journal_mode = WAL";
    private const string PragmaSynchronous = "PRAGMA synchronous = NORMAL";
    private const string PragmaForeignKeys = "PRAGMA foreign_keys = ON";
    private const string PragmaTempStore = "PRAGMA temp_store = MEMORY";
    private const string PragmaBusyTimeout = "PRAGMA busy_timeout = 5000";

    private const string CreateRunsTable = """
        CREATE TABLE IF NOT EXISTS runs (
          run_id         BLOB NOT NULL
                         CHECK (length(run_id) = 16)
                         PRIMARY KEY,
          started_at_utc TEXT NOT NULL,
          ended_at_utc   TEXT NULL,
          version        TEXT NOT NULL,
          build_config   TEXT NOT NULL,
          args           TEXT NOT NULL,
          hostname       TEXT NOT NULL,
          pid            INTEGER NOT NULL,
          os_version     TEXT NULL,
          dropped_count  INTEGER NOT NULL DEFAULT 0
        ) STRICT
        """;

    private const string CreateEventsTable = """
        CREATE TABLE IF NOT EXISTS events (
          id           INTEGER PRIMARY KEY AUTOINCREMENT,
          run_id       BLOB NOT NULL
                       CHECK (length(run_id) = 16)
                       REFERENCES runs(run_id) ON DELETE CASCADE,
          ts_utc       TEXT NOT NULL,
          ts_unix_ns   INTEGER NOT NULL,
          level        INTEGER NOT NULL,
          subsystem    TEXT NOT NULL,
          step         TEXT NOT NULL,
          session_id   BLOB NULL
                       CHECK (session_id IS NULL OR length(session_id) = 16),
          frame_seq    INTEGER NULL,
          activity_id  TEXT NULL,
          fields_json  TEXT NULL,
          exception_json TEXT NULL
        ) STRICT
        """;

    private const string CreateIdxEventsRun = "CREATE INDEX IF NOT EXISTS idx_events_run      ON events(run_id, id)";
    private const string CreateIdxEventsSubTs =
        "CREATE INDEX IF NOT EXISTS idx_events_sub_ts   ON events(subsystem, ts_unix_ns)";
    private const string CreateIdxEventsLvlTs =
        "CREATE INDEX IF NOT EXISTS idx_events_lvl_ts   ON events(level, ts_unix_ns) WHERE level >= 3";
    private const string CreateIdxEventsActivity =
        "CREATE INDEX IF NOT EXISTS idx_events_activity ON events(activity_id) WHERE activity_id IS NOT NULL";
    private const string CreateIdxRunsStarted =
        "CREATE INDEX IF NOT EXISTS idx_runs_started    ON runs(started_at_utc)";

    /// <summary>
    /// Apply the schema to an already-open connection. Idempotent.
    /// </summary>
    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ApplyPragmas(connection);
        ApplyTables(connection);
        ApplyIndices(connection);
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = PragmaJournalMode;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = PragmaSynchronous;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = PragmaForeignKeys;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = PragmaTempStore;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = PragmaBusyTimeout;
            cmd.ExecuteNonQuery();
        }
    }

    private static void ApplyTables(SqliteConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateRunsTable;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateEventsTable;
            cmd.ExecuteNonQuery();
        }
    }

    private static void ApplyIndices(SqliteConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateIdxEventsRun;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateIdxEventsSubTs;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateIdxEventsLvlTs;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateIdxEventsActivity;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = CreateIdxRunsStarted;
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// The authoritative DDL sequence in execution order. Exposed
    /// <see langword="internal"/> so the test assembly (via
    /// <c>InternalsVisibleTo</c>) can canonicalize these literals and
    /// diff them against <c>schema.sql</c>.
    /// </summary>
    internal static IEnumerable<string> StatementsForTest()
    {
        yield return PragmaJournalMode;
        yield return PragmaSynchronous;
        yield return PragmaForeignKeys;
        yield return PragmaTempStore;
        yield return PragmaBusyTimeout;
        yield return CreateRunsTable;
        yield return CreateEventsTable;
        yield return CreateIdxEventsRun;
        yield return CreateIdxEventsSubTs;
        yield return CreateIdxEventsLvlTs;
        yield return CreateIdxEventsActivity;
        yield return CreateIdxRunsStarted;
    }
}
