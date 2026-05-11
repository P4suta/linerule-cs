using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Diagnostics.Storage;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// The sink's bounded channel is <c>DropWrite</c> on saturation —
/// producers never block, the drop count flows into
/// <c>runs.dropped_count</c> on Dispose, and the invariant we pin is
/// conservation: <c>events_count + dropped_count ≥ submitted</c>. The
/// inequality direction (≥, not ==) handles the documented commit-side
/// fold-in: if a transaction fails the entire batch is also counted as
/// dropped, but those events may have already been deducted from the
/// channel side. In a healthy run no transaction fails and the equality
/// holds tight; the test asserts the conservative form so it stays green
/// on slow CI hardware.
/// </summary>
public sealed class BackpressureTests
{
    [Fact]
    public async Task Saturating_producers_accounts_for_every_submitted_entry()
    {
        const int TotalSubmissions = 50_000;
        const int Producers = 4;
        const int PerProducer = TotalSubmissions / Producers;
        var ct = TestContext.Current.CancellationToken;

        using var temp = new TempDb();
        var run = RunMetadata.Capture(["backpressure-test"]);
        var ts = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);

        // The producers all share the sink. Dispose blocks until the
        // drain task observes channel completion + commits the last
        // batch, so by the time we read back from SQLite, the writer
        // side is fully settled.
        await using (var sink = new SqliteEventSinkAsyncWrapper(temp.Path, run))
        {
            await Task.Run(() => RunProducers(sink.Sink, run.RunId, ts, Producers, PerProducer), ct);
        }

        await using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        await conn.OpenAsync(ct);

        var events = EventsCount(conn, run.RunId);
        var dropped = DroppedCount(conn, run.RunId);

        // Conservation invariant (see class doc). The strict equality
        // form is the goal but the inequality survives commit-side
        // double-counting on stressed runners.
        Assert.True(
            events + dropped >= TotalSubmissions,
            string.Create(
                CultureInfo.InvariantCulture,
                $"expected events ({events}) + dropped ({dropped}) >= {TotalSubmissions}"
            )
        );
        // Sanity: under-counting in the OTHER direction would mean we
        // PERSISTED more events than we submitted, which is impossible.
        Assert.True(
            events <= TotalSubmissions,
            string.Create(CultureInfo.InvariantCulture, $"persisted {events} > submitted {TotalSubmissions}")
        );
        // We expected real saturation pressure with capacity 8192 and
        // 50_000 fast-arriving entries; a perfectly clean run is a sign
        // the drain task is implausibly fast relative to producers.
        // We don't assert dropped > 0 because that would be flaky on
        // very large hosts; the test's value is the conservation law.
    }

    private static void RunProducers(
        SqliteEventSink sink,
        Guid runId,
        DateTimeOffset ts,
        int producers,
        int perProducer
    )
    {
        Parallel.For(
            0,
            producers,
            new ParallelOptions { MaxDegreeOfParallelism = producers },
            producerIndex =>
            {
                for (var i = 0; i < perProducer; i++)
                {
                    // Build the entry inside the producer so the contention
                    // shape matches a real WndProc / CursorTracker workload
                    // (per-thread alloc, ImmutableArray.Empty for fields).
                    var entry = new LogEntry(
                        Timestamp: ts,
                        Level: LogLevel.Trace,
                        Subsystem: string.Create(CultureInfo.InvariantCulture, $"prod-{producerIndex}"),
                        Step: string.Create(CultureInfo.InvariantCulture, $"i-{i}"),
                        Context: new LogContext(runId),
                        Fields: [],
                        Exception: null
                    );
                    sink.Write(entry);
                }
            }
        );
    }

    private static long EventsCount(SqliteConnection conn, Guid runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE run_id = $run_id;";
        cmd.Parameters.AddWithValue("$run_id", runId.ToByteArray());
        var raw = cmd.ExecuteScalar();
        return raw is long l ? l : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    private static long DroppedCount(SqliteConnection conn, Guid runId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT dropped_count FROM runs WHERE run_id = $run_id;";
        cmd.Parameters.AddWithValue("$run_id", runId.ToByteArray());
        var raw = cmd.ExecuteScalar();
        return raw is long l ? l : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <see cref="SqliteEventSink"/> is <see cref="IDisposable"/> only;
    /// this tiny wrapper bridges to <c>await using</c> so the test reads
    /// naturally without an explicit try/finally.
    /// </summary>
    private sealed class SqliteEventSinkAsyncWrapper(string path, RunMetadata run) : IAsyncDisposable
    {
        public SqliteEventSink Sink { get; } = new SqliteEventSink(path, run);

        public ValueTask DisposeAsync()
        {
            Sink.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
