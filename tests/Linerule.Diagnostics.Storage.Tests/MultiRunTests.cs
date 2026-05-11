using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Linerule.Diagnostics.Storage;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// One DB file may accumulate many runs across launches; the events of
/// each run must stay tagged with the run that produced them, and a
/// clean Dispose must populate <c>ended_at_utc</c>. This is the only
/// place run-id partitioning is verified end to end — the schema test
/// catches absence of the column, but only here do we exercise the
/// "two sequential openings against the same file" path.
/// </summary>
public sealed class MultiRunTests
{
    [Fact]
    public void Sequential_runs_against_one_db_partition_events_and_stamp_ended_at()
    {
        using var temp = new TempDb();
        var ts = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);

        var first = RunMetadata.Capture(["run-1"]);
        using (var sink = new SqliteEventSink(temp.Path, first))
        {
            WriteN(sink, first.RunId, ts, n: 3, subsystemTag: "S1");
        }

        var second = RunMetadata.Capture(["run-2"]);
        // Defensive: Capture's Logger.RunId fallback to NewGuid means the
        // two captures must produce distinct IDs in test context where
        // Logger is uninitialized. Pin it loudly — if Capture's contract
        // ever changes, this test should be the first thing that fails.
        Assert.NotEqual(first.RunId, second.RunId);
        using (var sink = new SqliteEventSink(temp.Path, second))
        {
            WriteN(sink, second.RunId, ts.AddSeconds(1), n: 3, subsystemTag: "S2");
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        conn.Open();
        AssertRunCounts(conn);
        AssertEventPartitioning(conn, first.RunId, second.RunId);
        AssertEndedAtSet(conn, first.RunId, second.RunId);
    }

    private static void AssertRunCounts(SqliteConnection conn)
    {
        long runCount;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM runs;";
            runCount = (long)cmd.ExecuteScalar()!;
        }
        Assert.Equal(2L, runCount);

        long totalEvents;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM events;";
            totalEvents = (long)cmd.ExecuteScalar()!;
        }
        Assert.Equal(6L, totalEvents);

        long totalDropped;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT SUM(dropped_count) FROM runs;";
            totalDropped = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        Assert.Equal(0L, totalDropped);
    }

    private static void AssertEventPartitioning(SqliteConnection conn, Guid first, Guid second)
    {
        Assert.Equal(3L, CountEventsForRun(conn, first));
        Assert.Equal(3L, CountEventsForRun(conn, second));
        Assert.Equal(["S1"], DistinctSubsystemsForRun(conn, first));
        Assert.Equal(["S2"], DistinctSubsystemsForRun(conn, second));
    }

    private static void AssertEndedAtSet(SqliteConnection conn, Guid first, Guid second)
    {
        Assert.False(
            string.IsNullOrEmpty(EndedAtForRun(conn, first)),
            "first run's ended_at_utc must be set after clean Dispose"
        );
        Assert.False(
            string.IsNullOrEmpty(EndedAtForRun(conn, second)),
            "second run's ended_at_utc must be set after clean Dispose"
        );
    }

    private static long CountEventsForRun(SqliteConnection conn, Guid runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE run_id = $run_id;";
        cmd.Parameters.AddWithValue("$run_id", runId.ToByteArray());
        return (long)cmd.ExecuteScalar()!;
    }

    private static List<string> DistinctSubsystemsForRun(SqliteConnection conn, Guid runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT subsystem FROM events WHERE run_id = $run_id;";
        cmd.Parameters.AddWithValue("$run_id", runId.ToByteArray());
        using var rdr = cmd.ExecuteReader();
        var list = new List<string>();
        while (rdr.Read())
        {
            list.Add(rdr.GetString(0));
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static string? EndedAtForRun(SqliteConnection conn, Guid runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ended_at_utc FROM runs WHERE run_id = $run_id;";
        cmd.Parameters.AddWithValue("$run_id", runId.ToByteArray());
        return cmd.ExecuteScalar() as string;
    }

    private static void WriteN(SqliteEventSink sink, Guid runId, DateTimeOffset baseTs, int n, string subsystemTag)
    {
        for (var i = 0; i < n; i++)
        {
            sink.Write(
                new LogEntry(
                    Timestamp: baseTs.AddMilliseconds(i),
                    Level: LogLevel.Info,
                    Subsystem: subsystemTag,
                    Step: string.Create(CultureInfo.InvariantCulture, $"step-{i}"),
                    Context: new LogContext(runId),
                    Fields: [],
                    Exception: null
                )
            );
        }
    }
}
