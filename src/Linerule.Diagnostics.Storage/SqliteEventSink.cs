using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Diagnostics.Internal;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Writer-only SQLite event store. Replaces <c>JsonlFileSink</c>.
///
/// <para>
/// <b>Architecture</b>: producer-consumer via a bounded
/// <see cref="Channel{T}"/>. Producers (any thread that calls
/// <see cref="Write"/>) push <see cref="LogEntry"/> values without
/// blocking; a single long-running drain task batches up to 256 entries per
/// transaction and writes them to SQLite. On saturation we drop-write
/// (counted in <see cref="_dropped"/>) rather than block the producer —
/// blocking the WndProc or render thread to log is worse than losing a few
/// events. The dropped count is persisted to <c>runs.dropped_count</c> on
/// <see cref="Dispose"/> so downstream tooling can detect lossy runs.
/// </para>
///
/// <para>
/// <b>Durability</b>: PRAGMAs <c>journal_mode=WAL</c> +
/// <c>synchronous=NORMAL</c> give crash-safety with reasonable throughput;
/// <c>wal_checkpoint(TRUNCATE)</c> is invoked once a minute from the drain
/// loop and once on <see cref="Dispose"/> so the WAL doesn't grow without
/// bound during long sessions.
/// </para>
///
/// <para>
/// <b>Multi-process safety</b>: <c>busy_timeout=5000</c> in
/// <c>schema.sql</c> means a concurrent reader (DuckDB, sqlite3 CLI) holds
/// the writer off for at most 5 s rather than failing immediately with
/// SQLITE_BUSY.
/// </para>
///
/// <para>
/// <b>SQL injection</b>: every <c>CommandText</c> in this file is a const
/// string; all parameterised values flow through <see cref="SqliteParameter"/>.
/// The <c>ban-sql-interpolation</c> rule in <c>xtask strict-code</c>
/// enforces this — string-interpolated CommandText assignment is a
/// lint-time error workspace-wide.
/// </para>
/// </summary>
public sealed partial class SqliteEventSink : IPathfulSink, IDisposable
{
    /// <summary>
    /// Absolute path of the SQLite event store this sink writes to. Read
    /// once by <c>LoggerRoot</c> for the crash-dump <c>ctx.db</c> field.
    /// Exposed via <see cref="IPathfulSink"/> so the dispatch is typed and
    /// AOT-safe (ADR-0010).
    /// </summary>
    public string Path { get; }

    // ── SQL ──────────────────────────────────────────────────────────────
    // All command text lives here as constants. Parameter names use SQLite's
    // `$name` form (also accepted by Microsoft.Data.Sqlite).

    private const string InsertRunSql = """
        INSERT INTO runs
          (run_id, started_at_utc, version, build_config, args, hostname, pid, os_version, dropped_count)
        VALUES
          ($run_id, $started, $version, $config, $args, $hostname, $pid, $os, 0);
        """;

    private const string InsertEventSql = """
        INSERT INTO events
          (run_id, ts_utc, ts_unix_ns, level, subsystem, step,
           session_id, frame_seq, activity_id, fields_json, exception_json)
        VALUES
          ($run_id, $ts_utc, $ts_ns, $level, $subsystem, $step,
           $session_id, $frame_seq, $activity_id, $fields_json, $exception_json);
        """;

    private const string UpdateRunEndSql = """
        UPDATE runs
        SET ended_at_utc = $end, dropped_count = $drop
        WHERE run_id = $run_id;
        """;

    private const string CheckpointSql = "PRAGMA wal_checkpoint(TRUNCATE);";

    // ── Tunables ─────────────────────────────────────────────────────────

    private const int ChannelCapacity = 8192;
    private const int BatchSize = 256;
    private const long CheckpointPeriodMs = 60_000;
    private const int DrainShutdownWaitSeconds = 5;

    // STJ writer options shared across emit calls; SkipValidation is safe
    // because the schema is fully closed by JsonFieldWriter.
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, SkipValidation = true };

    // ── State ────────────────────────────────────────────────────────────

    private readonly SqliteConnection _conn;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _drainTask;
    private readonly CancellationTokenSource _stop = new();
    private readonly Guid _runId;
    private readonly TimeProvider _time;
    private long _dropped;
    private bool _disposed;

    /// <summary>
    /// Open (or create) the events database at <paramref name="path"/>,
    /// apply the schema, insert the <c>runs</c> row for this process, then
    /// start the drain loop.
    /// </summary>
    /// <param name="path">Absolute path to the <c>.sqlite</c> file.</param>
    /// <param name="run">Captured per-process identity row.</param>
    /// <param name="timeProvider">
    /// Clock source — defaults to <see cref="TimeProvider.System"/>; tests
    /// pass <c>FakeTimeProvider</c> for deterministic <c>ended_at_utc</c>.
    /// </param>
    public SqliteEventSink(string path, RunMetadata run, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(run);

        Path = path;
        _runId = run.RunId;
        _time = timeProvider ?? TimeProvider.System;

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        };
        _conn = new SqliteConnection(cs.ToString());
        _conn.Open();

        SqliteSchema.Apply(_conn);
        InsertRunRow(_conn, run);

        _channel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                // Wait + TryWrite gives non-blocking producer + observable drops.
                // DropWrite would silently drop on saturation and TryWrite would
                // always return true — `_dropped` would never increment and the
                // backpressure contract would be a fiction.
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        _drainTask = Task
            .Factory.StartNew(DrainLoopAsync, _stop.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default)
            .Unwrap();
    }

    // ── ILogSink ─────────────────────────────────────────────────────────

    public void Write(in LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }
        // Non-blocking publish — on saturation we drop-write and count
        // the loss, because a blocked WndProc is a worse failure mode
        // than a missing event.
        if (!_channel.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Sinks own their durability: we don't expose a synchronous "flush
    /// the queue to disk now" because the drain loop is single-reader and
    /// no public surface lets callers safely synchronize on it without
    /// risking dispatcher-thread deadlock. The drain task picks entries up
    /// continuously; the only "flush" with semantic meaning is
    /// <see cref="Dispose"/>.
    /// </summary>
    public void Flush()
    {
        // Intentional no-op. See xmldoc.
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _channel.Writer.TryComplete();
        try
        {
            _drainTask.Wait(TimeSpan.FromSeconds(DrainShutdownWaitSeconds), CancellationToken.None);
        }
        catch (AggregateException)
        {
            // OperationCanceledException et al. — drain loop swallows but
            // Wait re-wraps. Logger sinks must never throw from teardown.
        }

        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = UpdateRunEndSql;
            cmd.Parameters.Add(
                NewParam("$end", SqliteType.Text, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture))
            );
            cmd.Parameters.Add(NewParam("$drop", SqliteType.Integer, Interlocked.Read(ref _dropped)));
            cmd.Parameters.Add(NewParam("$run_id", SqliteType.Blob, GuidToBlob(_runId)));
            cmd.ExecuteNonQuery();
            ExecuteCheckpoint();
        }
        catch (SqliteException)
        {
            // Disk full, db locked beyond busy_timeout, etc. Honor the
            // "never propagate from logger internals" invariant.
        }
        catch (IOException)
        {
            // Filesystem-level (the WAL checkpoint touches the file).
        }

        _conn.Dispose();
        _stop.Dispose();
    }

    // ── Drain loop ───────────────────────────────────────────────────────

    private async Task DrainLoopAsync()
    {
        var buf = new List<LogEntry>(BatchSize);
        var insert = PrepareEventInsert(_conn);
        try
        {
            var lastCheckpointAt = Environment.TickCount64;
            while (await _channel.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                buf.Clear();
                while (buf.Count < BatchSize && _channel.Reader.TryRead(out var e))
                {
                    buf.Add(e);
                }
                if (buf.Count == 0)
                {
                    continue;
                }

                try
                {
                    var tx = await _conn.BeginTransactionAsync(_stop.Token).ConfigureAwait(false);
                    await using (tx.ConfigureAwait(false))
                    {
                        insert.Transaction = (SqliteTransaction)tx;
                        foreach (var e in buf)
                        {
                            BindAndExec(insert, e);
                        }
                        await tx.CommitAsync(_stop.Token).ConfigureAwait(false);
                    }
                }
                catch (SqliteException)
                {
                    // Drop the batch but keep the loop alive. The runs
                    // table's dropped_count column normally reflects
                    // channel-side losses only — commit-side losses are
                    // folded in here too so the persisted count stays
                    // honest about every event the writer failed to keep.
                    Interlocked.Add(ref _dropped, buf.Count);
                }

                if (Environment.TickCount64 - lastCheckpointAt > CheckpointPeriodMs)
                {
                    ExecuteCheckpoint();
                    lastCheckpointAt = Environment.TickCount64;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown path: _stop was cancelled via Dispose.
        }
        finally
        {
            await insert.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ── Static helpers ───────────────────────────────────────────────────

    private static void InsertRunRow(SqliteConnection conn, RunMetadata run)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = InsertRunSql;
        cmd.Parameters.Add(NewParam("$run_id", SqliteType.Blob, GuidToBlob(run.RunId)));
        cmd.Parameters.Add(
            NewParam("$started", SqliteType.Text, run.StartedAt.ToString("O", CultureInfo.InvariantCulture))
        );
        cmd.Parameters.Add(NewParam("$version", SqliteType.Text, run.Version));
        cmd.Parameters.Add(NewParam("$config", SqliteType.Text, run.BuildConfig));
        cmd.Parameters.Add(NewParam("$args", SqliteType.Text, run.Args));
        cmd.Parameters.Add(NewParam("$hostname", SqliteType.Text, run.Hostname));
        cmd.Parameters.Add(NewParam("$pid", SqliteType.Integer, (long)run.Pid));
        cmd.Parameters.Add(NewParam("$os", SqliteType.Text, (object?)run.OsVersion ?? DBNull.Value));
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand PrepareEventInsert(SqliteConnection conn)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = InsertEventSql;
        // Pre-add parameters with null placeholders; BindAndExec will
        // overwrite Value on each iteration. Pre-allocating avoids
        // re-parsing the parameter list every row.
        cmd.Parameters.Add(NewParam("$run_id", SqliteType.Blob, DBNull.Value));
        cmd.Parameters.Add(NewParam("$ts_utc", SqliteType.Text, DBNull.Value));
        cmd.Parameters.Add(NewParam("$ts_ns", SqliteType.Integer, DBNull.Value));
        cmd.Parameters.Add(NewParam("$level", SqliteType.Integer, DBNull.Value));
        cmd.Parameters.Add(NewParam("$subsystem", SqliteType.Text, DBNull.Value));
        cmd.Parameters.Add(NewParam("$step", SqliteType.Text, DBNull.Value));
        cmd.Parameters.Add(NewParam("$session_id", SqliteType.Blob, DBNull.Value));
        cmd.Parameters.Add(NewParam("$frame_seq", SqliteType.Integer, DBNull.Value));
        cmd.Parameters.Add(NewParam("$activity_id", SqliteType.Text, DBNull.Value));
        cmd.Parameters.Add(NewParam("$fields_json", SqliteType.Text, DBNull.Value));
        cmd.Parameters.Add(NewParam("$exception_json", SqliteType.Text, DBNull.Value));
        cmd.Prepare();
        return cmd;
    }

    private void BindAndExec(SqliteCommand insert, in LogEntry e)
    {
        insert.Parameters["$run_id"].Value = GuidToBlob(_runId);
        insert.Parameters["$ts_utc"].Value = e.Timestamp.ToString("O", CultureInfo.InvariantCulture);
        // DateTime ticks are 100 ns; ts_unix_ns is nanoseconds from the Unix epoch.
        insert.Parameters["$ts_ns"].Value =
            (e.Timestamp.ToUnixTimeMilliseconds() * 1_000_000L)
            + ((e.Timestamp.UtcTicks % TimeSpan.TicksPerMillisecond) * 100L);
        insert.Parameters["$level"].Value = (long)(int)e.Level;
        insert.Parameters["$subsystem"].Value = e.Subsystem;
        insert.Parameters["$step"].Value = e.Step;
        insert.Parameters["$session_id"].Value = e.Context.SessionId is Guid sid ? GuidToBlob(sid) : DBNull.Value;
        insert.Parameters["$frame_seq"].Value = e.Context.FrameSeq is long fs ? fs : DBNull.Value;
        insert.Parameters["$activity_id"].Value = (object?)e.Context.ActivityId ?? DBNull.Value;
        insert.Parameters["$fields_json"].Value = (object?)SerializeFields(e.Fields) ?? DBNull.Value;
        insert.Parameters["$exception_json"].Value = (object?)SerializeException(e.Exception) ?? DBNull.Value;
        insert.ExecuteNonQuery();
    }

    private void ExecuteCheckpoint()
    {
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = CheckpointSql;
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Best-effort: a checkpoint failure (e.g. reader holding the
            // WAL) is recoverable on the next tick.
        }
    }

    private static string? SerializeFields(System.Collections.Immutable.ImmutableArray<LogField> fields)
    {
        if (fields.IsDefaultOrEmpty)
        {
            return null;
        }
        var buf = new ArrayBufferWriter<byte>(128);
        using (var w = new Utf8JsonWriter(buf, WriterOptions))
        {
            w.WriteStartObject();
            foreach (var f in fields)
            {
                JsonFieldWriter.Write(w, f);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static string? SerializeException(Exception? ex)
    {
        if (ex is null)
        {
            return null;
        }
        // Mirrors JsonlFileSink.WriteException so analysts see the same
        // shape across JSONL fixture files and the SQLite store.
        var buf = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buf, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("type", ex.GetType().FullName);
            w.WriteString("message", ex.Message);
            w.WriteString("stack", ex.StackTrace ?? string.Empty);
            if (ex.InnerException is { } inner)
            {
                w.WritePropertyName("inner");
                w.WriteStartObject();
                w.WriteString("type", inner.GetType().FullName);
                w.WriteString("message", inner.Message);
                w.WriteString("stack", inner.StackTrace ?? string.Empty);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static byte[] GuidToBlob(Guid g) => g.ToByteArray();

    private static SqliteParameter NewParam(string name, SqliteType type, object value) =>
        new(name, type) { Value = value };
}
