using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Linerule.Diagnostics.Storage;
using Linerule.Platform.Windows.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// "Write → drain → SELECT back" preserves every column the writer is
/// expected to populate. The typed columns are asserted directly; the
/// JSON columns are parsed and probed by field (we don't pin byte
/// equality on JSON because property ordering in
/// <see cref="System.Text.Json"/> output isn't a stable contract).
/// </summary>
public sealed class RoundtripTests
{
    // MA0176 wants binary Guid construction (compile-time, no runtime parse).
    // Bytes mirror the GUIDs 11111111-2222-3333-4444-555555555555 and
    // aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee respectively (each Guid happens to
    // be byte-palindromic per field, so endianness doesn't matter).
    private static readonly Guid SessionId = new([
        0x11,
        0x11,
        0x11,
        0x11,
        0x22,
        0x22,
        0x33,
        0x33,
        0x44,
        0x44,
        0x55,
        0x55,
        0x55,
        0x55,
        0x55,
        0x55,
    ]);
    private static readonly Guid FieldGuid = new([
        0xaa,
        0xaa,
        0xaa,
        0xaa,
        0xbb,
        0xbb,
        0xcc,
        0xcc,
        0xdd,
        0xdd,
        0xee,
        0xee,
        0xee,
        0xee,
        0xee,
        0xee,
    ]);
    private static readonly DateTimeOffset Ts = new(2026, 5, 11, 12, 34, 56, 789, TimeSpan.Zero);

    [Fact]
    public void Logentry_with_diverse_field_types_roundtrips_through_drain()
    {
        using var temp = new TempDb();
        var run = RunMetadata.Capture(["roundtrip-test"]);
        var entry = BuildDiverseEntry(run.RunId);

        using (var sink = new SqliteEventSink(temp.Path, run))
        {
            // Dispose completes the channel, awaits drain, writes ended_at_utc.
            sink.Write(entry);
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT level, subsystem, step, ts_utc, ts_unix_ns,
                   session_id, frame_seq, activity_id,
                   fields_json, exception_json
            FROM events
            WHERE run_id = $run_id;
            """;
        cmd.Parameters.AddWithValue("$run_id", run.RunId.ToByteArray());
        using var rdr = cmd.ExecuteReader();
        Assert.True(rdr.Read(), "expected exactly one event row after Dispose");
        AssertTypedColumns(rdr, Ts, SessionId);
        AssertFieldsJson(rdr.GetString(8), FieldGuid);
        AssertExceptionJson(rdr.GetString(9));
        Assert.False(rdr.Read(), "expected exactly one event row, not more");
    }

    private static LogEntry BuildDiverseEntry(Guid runId) =>
        new(
            Timestamp: Ts,
            Level: LogLevel.Warn,
            Subsystem: "RoundtripSubsystem",
            Step: "step-7",
            Context: new LogContext(RunId: runId, SessionId: SessionId, FrameSeq: 42L, ActivityId: "act-abc"),
            Fields:
            [
                new LogField("str", "hello"),
                new LogField("int", 123),
                new LogField("long", 9_999_999_999L),
                new LogField("dbl", 3.14d),
                new LogField("flag", Value: true),
                new LogField("guid", FieldGuid),
                new LogField("when", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
                new LogField("missing", Value: null),
            ],
            Exception: MakeNestedException()
        );

    private static void AssertTypedColumns(SqliteDataReader rdr, DateTimeOffset ts, Guid sessionId)
    {
        Assert.Equal((long)LogLevel.Warn, rdr.GetInt64(0));
        Assert.Equal("RoundtripSubsystem", rdr.GetString(1));
        Assert.Equal("step-7", rdr.GetString(2));
        Assert.Equal(ts.ToString("O", CultureInfo.InvariantCulture), rdr.GetString(3));

        var expectedNs =
            (ts.ToUnixTimeMilliseconds() * 1_000_000L) + ((ts.UtcTicks % TimeSpan.TicksPerMillisecond) * 100L);
        Assert.Equal(expectedNs, rdr.GetInt64(4));

        // session_id is stored as 16-byte BLOB (Guid.ToByteArray()).
        var sidBlob = (byte[])rdr["session_id"];
        Assert.Equal(sessionId, new Guid(sidBlob));

        Assert.Equal(42L, rdr.GetInt64(6));
        Assert.Equal("act-abc", rdr.GetString(7));
    }

    private static void AssertFieldsJson(string fieldsJson, Guid fieldGuid)
    {
        // fields_json: parse and probe by key. Order is not guaranteed.
        using var doc = JsonDocument.Parse(fieldsJson);
        var root = doc.RootElement;
        Assert.Equal("hello", root.GetProperty("str").GetString());
        Assert.Equal(123, root.GetProperty("int").GetInt32());
        Assert.Equal(9_999_999_999L, root.GetProperty("long").GetInt64());
        Assert.Equal(3.14d, root.GetProperty("dbl").GetDouble());
        Assert.True(root.GetProperty("flag").GetBoolean());
        Assert.Equal(fieldGuid.ToString("D"), root.GetProperty("guid").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("missing").ValueKind);
        // DateTimeOffset is "O" formatted; we check the leading
        // calendar-and-time portion to stay robust against
        // platform-trailing variants.
        var whenStr = root.GetProperty("when").GetString();
        Assert.NotNull(whenStr);
        Assert.StartsWith("2026-01-02T03:04:05", whenStr, StringComparison.Ordinal);
    }

    private static void AssertExceptionJson(string exceptionJson)
    {
        // exception_json carries the type, the message, and the inner.
        using var doc = JsonDocument.Parse(exceptionJson);
        var root = doc.RootElement;
        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("type").GetString());
        Assert.Equal("outer-boom", root.GetProperty("message").GetString());
        var inner = root.GetProperty("inner");
        Assert.Equal(typeof(FormatException).FullName, inner.GetProperty("type").GetString());
        Assert.Equal("inner-boom", inner.GetProperty("message").GetString());
    }

    [Fact]
    public void Empty_fields_array_persists_as_null_fields_json()
    {
        // The sink elides the JSON envelope when the entry has no fields,
        // so analysts get NULL not "{}". That is a documented column
        // contract — pin it.
        using var temp = new TempDb();
        var run = RunMetadata.Capture(["roundtrip-empty"]);
        var ts = new DateTimeOffset(2026, 5, 11, 0, 0, 0, TimeSpan.Zero);

        var entry = new LogEntry(
            Timestamp: ts,
            Level: LogLevel.Info,
            Subsystem: "Bare",
            Step: "boot",
            Context: new LogContext(run.RunId),
            Fields: [],
            Exception: null
        );

        using (var sink = new SqliteEventSink(temp.Path, run))
        {
            sink.Write(entry);
        }

        using var conn = new SqliteConnection($"Data Source={temp.Path};Mode=ReadOnly;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT fields_json, exception_json, session_id, frame_seq, activity_id
            FROM events
            WHERE run_id = $run_id;
            """;
        cmd.Parameters.AddWithValue("$run_id", run.RunId.ToByteArray());
        using var rdr = cmd.ExecuteReader();
        Assert.True(rdr.Read());
        Assert.True(rdr.IsDBNull(0), "fields_json should be NULL when Fields is empty");
        Assert.True(rdr.IsDBNull(1), "exception_json should be NULL when Exception is null");
        Assert.True(rdr.IsDBNull(2), "session_id should be NULL when context has none");
        Assert.True(rdr.IsDBNull(3), "frame_seq should be NULL when context has none");
        Assert.True(rdr.IsDBNull(4), "activity_id should be NULL when context has none");
    }

    private static InvalidOperationException MakeNestedException()
    {
        try
        {
            try
            {
                throw new FormatException("inner-boom");
            }
            catch (FormatException inner)
            {
                throw new InvalidOperationException("outer-boom", inner);
            }
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }
}
